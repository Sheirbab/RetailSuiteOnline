using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Customer.Entities;
using RetailSuite.Infrastructure.Modules.Customer.Services;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Orders.Dtos;
using RetailSuite.Infrastructure.Modules.Tax.Services;
using RetailSuite.Infrastructure.Email;
using RetailSuite.Infrastructure.Seeders;
using RetailSuite.Modules.Accounting.Entities;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Orders.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Orders.Services
{
    /// <summary>
    /// Cash-counter POS service. End-to-end:
    ///   1. Decrement inventory + capture cost.
    ///   2. Build the Order (with line + order discounts).
    ///   3. Apply store-credit and loyalty redemptions (validate against ledger balances).
    ///   4. Confirm + complete + register cash payment.
    ///   5. Earn loyalty on the gross sale.
    ///   6. Post the GL journal entry (cash + revenue + COGS + tax).
    ///   7. Tidy up: delete the held-sale row if this was a resume.
    /// All steps share one DB transaction so partial sales are impossible.
    /// </summary>
    public class SaleService
    {
        private readonly RetailDbContext _db;
        private readonly AccountingService _accountingService;
        private readonly IEmailService _emailService;
        private readonly IStoreCreditService _storeCredit;
        private readonly ILoyaltyService _loyalty;
        private readonly ICurrentUserContext _currentUser;
        private readonly IInvoiceStampingService _invoices;

        public SaleService(
            RetailDbContext db,
            AccountingService accountingService,
            IEmailService emailService,
            IStoreCreditService storeCredit,
            ILoyaltyService loyalty,
            ICurrentUserContext currentUser,
            IInvoiceStampingService invoices)
        {
            _db = db;
            _accountingService = accountingService;
            _emailService = emailService;
            _storeCredit = storeCredit;
            _loyalty = loyalty;
            _currentUser = currentUser;
            _invoices = invoices;
        }

        public async Task<Guid> ProcessPosSaleAsync(CreatePosSaleRequest request)
        {
            try
            {
                if (request.Items == null || request.Items.Count == 0)
                    throw new BusinessRuleException("Cart is empty.");

                var tenantId  = _currentUser.TenantId;
                var cashierId = _currentUser.UserId;

                // Has the cashier attached a real customer? If not, fall back to the
                // tenant's auto-seeded Walk-in Customer row so the Order FK is always valid.
                // The "is walk-in" flag gates loyalty earn / store-credit redemption below —
                // we don't want walk-ins to accumulate loyalty.
                var explicitCustomerId = request.CustomerId;
                var hasRealCustomer    = explicitCustomerId.HasValue
                                      && explicitCustomerId.Value != Guid.Empty;

                var customerId = hasRealCustomer
                    ? explicitCustomerId!.Value
                    : await TenantDefaultsSeeder.GetWalkInCustomerIdAsync(_db, tenantId);

                // Resolve the selling location once for this sale.
                var sellingLocationId = await ResolveSellingLocationAsync(tenantId, request.LocationId);

                Order? order = null;
                decimal totalCogs = 0;
                decimal amountDueResult = 0;

                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                using var transaction = await _db.Database.BeginTransactionAsync();

                // 1. Order header
                var orderNumber = $"POS-{DateTime.UtcNow.Ticks}";
                order = new Order(orderNumber, customerId);
                // Stamp TenantId immediately — StampAsync (below) needs it to compute the
                // invoice sequence; the SaveChangesAsync belt-and-braces stamp runs too late.
                order.TenantId = tenantId;
                order.SetCashier(cashierId);

                totalCogs = 0;

                // 2. Cart lines — decrement stock at the selling location, write inventory ledger, add to order
                foreach (var lineReq in request.Items)
                {
                    var variant = await _db.ProductVariants
                        .FirstAsync(v => v.Id == lineReq.ProductVariantId);

                    var inventoryItem = await _db.InventoryItems
                        .FirstOrDefaultAsync(i => i.ProductVariantId == variant.Id
                                               && i.LocationId == sellingLocationId);

                    if (inventoryItem == null || inventoryItem.CurrentStock < lineReq.Quantity)
                    {
                        var have = inventoryItem?.CurrentStock ?? 0;
                        throw new BusinessRuleException(
                            $"Insufficient stock at this branch for {variant.SKU} — have {have}, need {lineReq.Quantity}.");
                    }

                    var costAmount = inventoryItem.IssueStock(lineReq.Quantity);
                    variant.AverageCost = inventoryItem.AverageCost;
                    totalCogs += costAmount;

                    // Defer the StockQuantity rollup recompute until after all lines are processed.

                    var orderItem = new OrderItem(
                        orderId: order.Id,
                        productVariantId: variant.Id,
                        sku: variant.SKU,
                        unitPrice: variant.Price,
                        quantity: lineReq.Quantity,
                        taxRate: variant.TaxRate,
                        lineDiscountAmount: Math.Max(0, lineReq.LineDiscountAmount));

                    order.AddItem(orderItem);

                    _db.InventoryTransactions.Add(new InventoryTransaction(
                        inventoryItem.Id,
                        variant.Id,
                        inventoryItem.LocationId,
                        -lineReq.Quantity,
                        InventoryTransactionType.Sale,
                        order.Id.ToString(),
                        "POS sale"));
                }

                // 2a. Flush the item-loop mutations to the store BEFORE the rollup
                //     query. On EF InMemory, uncommitted tracked-entity changes are
                //     NOT visible to LINQ queries, so SumAsync would see the pre-sale
                //     stock. On SQL Server (inside the surrounding transaction)
                //     read-your-writes hides the bug, but the flush is cheap either way.
                await _db.SaveChangesAsync();

                // 2b. Recompute the rollup denormalised onto ProductVariant.StockQuantity
                //     across all locations for every variant touched in this sale.
                var touchedVariantIds = request.Items.Select(i => i.ProductVariantId).Distinct().ToList();
                foreach (var vid in touchedVariantIds)
                {
                    var variantToUpdate = await _db.ProductVariants.FirstAsync(v => v.Id == vid);
                    variantToUpdate.StockQuantity = await _db.InventoryItems
                        .Where(i => i.ProductVariantId == vid)
                        .SumAsync(i => (int?)i.CurrentStock) ?? 0;
                }

                // 3. Order-level discount (after lines accumulated)
                if (request.OrderDiscountAmount > 0)
                    order.ApplyOrderDiscount(request.OrderDiscountAmount);

                // 4. Redemptions — validate ledger balances first
                if (request.StoreCreditRedeem > 0)
                {
                    if (!hasRealCustomer)
                        throw new BusinessRuleException("Cannot redeem store credit on a walk-in sale.");

                    // Records a NEGATIVE ledger entry; throws if insufficient.
                    await _storeCredit.RedeemAsync(
                        tenantId, customerId, request.StoreCreditRedeem,
                        order.Id, cashierId, $"POS sale {orderNumber}");

                    order.ApplyStoreCreditRedemption(request.StoreCreditRedeem);
                }

                if (request.LoyaltyPointsRedeem > 0)
                {
                    if (!hasRealCustomer)
                        throw new BusinessRuleException("Cannot redeem points on a walk-in sale.");

                    var redemption = await _loyalty.RedeemAsync(
                        tenantId, customerId, request.LoyaltyPointsRedeem,
                        order.Id, order.TotalAmount);

                    order.ApplyLoyaltyRedemption(redemption.PointsRedeemed, redemption.RupeesValue);
                }

                // 5. Validate cash collected covers the remaining amount due
                var amountDue = order.AmountDueAfterRedemptions;
                if (request.PaidAmount < amountDue)
                    throw new BusinessRuleException(
                        $"Insufficient cash: due {amountDue:N2}, received {request.PaidAmount:N2}.");

                // 6. Finalise — confirm + complete + stamp FBR-compliant invoice + register payment + earn loyalty
                order.Confirm();
                order.Complete();
                await _invoices.StampAsync(order);

                if (amountDue > 0)
                    order.RegisterPayment(amountDue);

                _db.Orders.Add(order);

                // The Payment row records what the cashier collected (= amountDue, the cash portion).
                // Fully qualified to disambiguate from the RetailSuite.Infrastructure.Modules.Payment namespace.
                if (amountDue > 0)
                {
                    _db.Payments.Add(new RetailSuite.Modules.Accounting.Entities.Payment(order.Id, amountDue, "Cash"));
                }

                // 7. Loyalty earn on the gross sale value (excluding redemptions — earn on cash they spent)
                if (hasRealCustomer)
                {
                    await _loyalty.EarnOnOrderAsync(tenantId, customerId, order.Id, order.TotalAmount);
                }

                // 7b. Change-as-store-credit. If the cashier collected more than due AND
                //     opted to keep the change as customer credit (instead of returning cash),
                //     post a positive StoreCreditTransaction so the customer is owed that
                //     amount on their ledger. Walk-in sales can never use this path.
                var changeAmount = Math.Max(0m, request.PaidAmount - amountDue);
                if (request.CreditChangeAsStoreCredit && hasRealCustomer && changeAmount > 0)
                {
                    await _storeCredit.IssueAsync(
                        tenantId:        tenantId,
                        customerId:      customerId,
                        amount:          changeAmount,
                        reason:          StoreCreditReason.ChangeAsCredit,
                        note:            $"Change kept as credit on sale {order.OrderNumber}",
                        orderId:         order.Id,
                        createdByUserId: cashierId);
                }
                else if (request.CreditChangeAsStoreCredit && !hasRealCustomer && changeAmount > 0)
                {
                    throw new BusinessRuleException(
                        "Cannot save change as store credit on a walk-in sale — attach a customer first.");
                }

                // 8. Accounting: book what cash actually moved.
                //    Revenue = amountDue (= cash collected). The redemption portion is tracked
                //    in StoreCreditTransactions + LoyaltyTransactions ledgers but is treated as
                //    a discount for GL purposes to keep the journal balanced without introducing
                //    a Customer Credit Liability account.
                if (amountDue > 0)
                {
                    // Self-heal: ensure baseline Chart of Accounts (and other defaults) exists
                    // for this tenant. Idempotent — only adds missing rows.
                    await TenantDefaultsSeeder.SeedAsync(_db, tenantId);

                    var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000");
                    var revenueAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "4000");
                    var inventoryAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1100");
                    var cogsAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "5000");

                    if (cashAccount == null || revenueAccount == null
                        || inventoryAccount == null || cogsAccount == null)
                    {
                        throw new BusinessRuleException(
                            "Chart of Accounts is incomplete for this tenant (missing 1000 Cash / "
                            + "4000 Revenue / 1100 Inventory / 5000 COGS). Visit the admin / accounts page "
                            + "or re-run the tenant seeder to fix this.");
                    }

                    // Recompute proportional tax on the cash portion only.
                    var taxRatio = order.TotalAmount > 0
                        ? Math.Min(1m, amountDue / order.TotalAmount)
                        : 0m;
                    var cashTax = order.TaxAmount * taxRatio;
                    var cashRevenue = amountDue - cashTax;

                    var journalLines = new List<(Guid, decimal, decimal)>
                {
                    (cashAccount.Id,      amountDue, 0),
                    (revenueAccount.Id,   0, cashRevenue),
                    (cogsAccount.Id,      totalCogs, 0),
                    (inventoryAccount.Id, 0, totalCogs)
                };

                    if (cashTax > 0)
                    {
                        var taxAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2000");
                        if (taxAccount == null)
                            throw new BusinessRuleException(
                                "Tax was charged on this sale but the '2000 Tax Payable' account "
                                + "does not exist for this tenant. Re-seed the chart of accounts.");
                        journalLines.Add((taxAccount.Id, 0, cashTax));
                    }

                    await _accountingService.CreateJournalEntryAsync(
                        order.Id.ToString(),
                        $"POS Sale {order.OrderNumber}",
                        journalLines);
                }

                // 9. Clean up resumed held sale, if any
                if (request.ResumedFromHeldSaleId.HasValue)
                {
                    var held = await _db.HeldSales
                        .FirstOrDefaultAsync(h => h.Id == request.ResumedFromHeldSaleId.Value);
                    if (held != null) _db.HeldSales.Remove(held);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                amountDueResult = amountDue;
                });

                // 10. Email receipt if customer email known (best-effort, outside the txn)
                string? receiptEmail = null;
                if (hasRealCustomer)
                {
                    var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
                    receiptEmail = customer?.Email;
                }

                if (!string.IsNullOrWhiteSpace(receiptEmail))
                {
                    var body = $@"
<h2>Receipt — {order!.OrderNumber}</h2>
<p>Thank you for your purchase!</p>
<p><strong>Items total:</strong> Rs {(order.TotalAmount + order.OrderDiscountAmount):N2}</p>
@if(order.OrderDiscountAmount > 0)<p><strong>Discount:</strong> &minus; Rs {order.OrderDiscountAmount:N2}</p>
<p><strong>Tax:</strong> Rs {order.TaxAmount:N2}</p>
<p><strong>Total:</strong> Rs {order.TotalAmount:N2}</p>
<p><strong>Paid in cash:</strong> Rs {amountDueResult:N2}</p>
@if(order.StoreCreditRedeemed > 0)<p><strong>Store credit used:</strong> Rs {order.StoreCreditRedeemed:N2}</p>
@if(order.LoyaltyRedeemedRupees > 0)<p><strong>Loyalty redeemed:</strong> Rs {order.LoyaltyRedeemedRupees:N2} ({order.LoyaltyPointsRedeemed} pts)</p>
<p><strong>Date:</strong> {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC</p>";
                    await _emailService.SendAsync(receiptEmail, $"Receipt: {order.OrderNumber}", body);
                }

                return order!.Id;

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
        }

        /// <summary>
        /// Resolve which branch this sale is happening at. Prefers the explicit
        /// location id sent by the POS terminal (from localStorage); falls back
        /// to the tenant's default location.
        /// </summary>
        private async Task<Guid> ResolveSellingLocationAsync(Guid tenantId, Guid? explicitLocationId)
        {
            if (explicitLocationId.HasValue && explicitLocationId.Value != Guid.Empty)
                return explicitLocationId.Value;

            var def = await _db.Locations
                .Where(l => l.IsDefault && l.IsActive)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync();
            if (!def.HasValue)
                throw new BusinessRuleException(
                    "POS terminal has no location bound and no default location is configured for this tenant.");
            return def.Value;
        }
    }
}
