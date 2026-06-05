using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Inventory.Entities;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.SupplierReturns.Entities;
using RetailSuite.Modules.Catalog.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.SupplierReturns.Services;

/// <summary>
/// Orchestrates the supplier-return workflow. Owns the side-effects that the
/// <see cref="SupplierReturn"/> aggregate intentionally does NOT do itself —
/// inventory deduction and credit-note issuance.
/// </summary>
public interface ISupplierReturnService
{
    Task<SupplierReturn> CreateDraftAsync(
        Guid supplierId,
        SupplierReturnReason reason,
        Guid? sourceReceivingOrderId,
        string? notes);

    Task<SupplierReturnItem> AddLineAsync(
        Guid returnId,
        Guid productVariantId,
        int quantity,
        decimal? unitCostOverride,
        string? notes);

    Task SubmitAsync(Guid returnId);

    /// <summary>
    /// Pre-fills the return from its <c>SourceReceivingOrderId</c> — adds a line for
    /// every received line on the source PO, defaulting qty to ReceivedQuantity and
    /// unit cost to the PO line's UnitCost. Skips variants that are already on the
    /// return (no duplicates). Throws if no source PO is set or the return is not in Draft.
    /// </summary>
    Task<int> PullFromSourceAsync(Guid returnId);

    /// <summary>
    /// Mark the return Completed. Deducts stock for every line and issues a
    /// SupplierCreditNote for the total value. Idempotent if already Completed.
    /// </summary>
    Task<SupplierCreditNote> CompleteAsync(Guid returnId);

    Task CancelAsync(Guid returnId);

    /// <summary>
    /// Apply <paramref name="amount"/> of <paramref name="creditNoteId"/> against
    /// <paramref name="receivingOrderId"/>. Both must belong to the same supplier.
    /// The credit's <c>AppliedAmount</c> is bumped and a SupplierCreditApplication
    /// record is created for audit.
    /// </summary>
    Task<SupplierCreditApplication> ApplyCreditAsync(
        Guid creditNoteId,
        Guid receivingOrderId,
        decimal amount,
        string? notes);
}

public class SupplierReturnService : ISupplierReturnService
{
    private readonly RetailDbContext _db;
    private readonly ISupplierReturnNumberGenerator _numbers;
    private readonly InventoryService _inventory;
    private readonly ILogger<SupplierReturnService> _logger;
    private readonly ITenantContext _tenantContext;

    public SupplierReturnService(
        RetailDbContext db,
        ISupplierReturnNumberGenerator numbers,
        InventoryService inventory,
        ITenantContext tenantContext,
        ILogger<SupplierReturnService> logger)
    {
        _db            = db;
        _numbers       = numbers;
        _inventory     = inventory;
        _tenantContext = tenantContext;
        _logger        = logger;
    }

    public async Task<SupplierReturn> CreateDraftAsync(
        Guid supplierId,
        SupplierReturnReason reason,
        Guid? sourceReceivingOrderId,
        string? notes)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var supplierExists = await _db.Suppliers.AnyAsync(s => s.Id == supplierId);
        if (!supplierExists)
            throw new NotFoundException("Supplier", supplierId);

        if (sourceReceivingOrderId.HasValue)
        {
            var poExists = await _db.ReceivingOrders.AnyAsync(r => r.Id == sourceReceivingOrderId.Value);
            if (!poExists)
                throw new NotFoundException("ReceivingOrder", sourceReceivingOrderId.Value);
        }

        var returnNumber = await _numbers.NextReturnNumberAsync(tenantId);
        var ret = new SupplierReturn(tenantId, returnNumber, supplierId, reason, sourceReceivingOrderId);
        if (!string.IsNullOrWhiteSpace(notes)) ret.SetNotes(notes);

        _db.SupplierReturns.Add(ret);
        await _db.SaveChangesAsync();
        return ret;
    }

    public async Task<SupplierReturnItem> AddLineAsync(
        Guid returnId,
        Guid productVariantId,
        int quantity,
        decimal? unitCostOverride,
        string? notes)
    {
        var ret = await _db.SupplierReturns
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == returnId)
            ?? throw new NotFoundException("SupplierReturn", returnId);

        if (ret.Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Only Draft returns can be modified.");

        var variant = await _db.ProductVariants
            .FirstOrDefaultAsync(v => v.Id == productVariantId)
            ?? throw new NotFoundException("ProductVariant", productVariantId);

        // Default cost: prefer current average cost (cleanest accounting),
        // falling back to the catalogue price if no purchase history exists.
        var cost = unitCostOverride
                ?? (variant.AverageCost > 0 ? variant.AverageCost : variant.Price);

        var line = new SupplierReturnItem(
            ret.TenantId, ret.Id, variant.Id, variant.SKU, quantity, cost, notes);
        ret.AddItem(line);

        await _db.SaveChangesAsync();
        return line;
    }

    public async Task SubmitAsync(Guid returnId)
    {
        var ret = await _db.SupplierReturns
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == returnId)
            ?? throw new NotFoundException("SupplierReturn", returnId);

        ret.Submit();
        await _db.SaveChangesAsync();
    }

    public async Task<int> PullFromSourceAsync(Guid returnId)
    {
        var ret = await _db.SupplierReturns
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == returnId)
            ?? throw new NotFoundException("SupplierReturn", returnId);

        if (ret.Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Only Draft returns can be pre-filled from a source PO.");
        if (!ret.SourceReceivingOrderId.HasValue)
            throw new BusinessRuleException("This return is not linked to a source receiving order.");

        var poItems = await _db.ReceivingOrderItems
            .Where(i => i.ReceivingOrderId == ret.SourceReceivingOrderId.Value)
            .ToListAsync();

        // Skip variants already on the return — caller can re-pull safely.
        var existingVariantIds = ret.Items.Select(i => i.ProductVariantId).ToHashSet();

        var added = 0;
        foreach (var poLine in poItems.Where(p => p.ReceivedQuantity > 0))
        {
            if (existingVariantIds.Contains(poLine.ProductVariantId)) continue;

            var line = new SupplierReturnItem(
                tenantId:         ret.TenantId,
                supplierReturnId: ret.Id,
                productVariantId: poLine.ProductVariantId,
                sku:              poLine.Sku,
                quantity:         poLine.ReceivedQuantity,
                unitCost:         poLine.UnitCost,
                notes:            null);
            ret.AddItem(line);
            added++;
        }

        if (added > 0) await _db.SaveChangesAsync();
        return added;
    }

    public async Task<SupplierCreditNote> CompleteAsync(Guid returnId)
    {
        var ret = await _db.SupplierReturns
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == returnId)
            ?? throw new NotFoundException("SupplierReturn", returnId);

        // Idempotency: if already Completed, just return the existing credit note.
        if (ret.Status == SupplierReturnStatus.Completed)
        {
            var existing = await _db.SupplierCreditNotes
                .FirstOrDefaultAsync(c => c.SupplierReturnId == ret.Id);
            if (existing != null) return existing;
        }

        if (ret.Status == SupplierReturnStatus.Cancelled)
            throw new BusinessRuleException("Cancelled returns cannot be completed.");
        if (ret.Items.Count == 0)
            throw new BusinessRuleException("Cannot complete a return with no items.");

        // 1. Verify stock — refuse to complete if we don't physically have the
        //    units recorded as in-stock. This catches double-returns and bad data.
        foreach (var line in ret.Items)
        {
            var current = await _inventory.GetStockAsync(line.ProductVariantId);
            if (current < line.Quantity)
                throw new BusinessRuleException(
                    $"Cannot return {line.Quantity} of {line.Sku} — only {current} on hand.");
        }

        // 2. Deduct inventory for every line. Each adjustment writes a ledger
        //    entry so the return is traceable from the SKU history view.
        foreach (var line in ret.Items)
        {
            await _inventory.AdjustStockAsync(
                productVariantId: line.ProductVariantId,
                quantityChange:   -line.Quantity,
                transactionType:  InventoryTransactionType.SupplierReturn,
                referenceId:      ret.ReturnNumber,
                notes:            $"Supplier return — {ret.Reason}");
        }

        // 3. Issue the credit note.
        var creditNoteNumber = await _numbers.NextCreditNoteNumberAsync(ret.TenantId);
        var credit = new SupplierCreditNote(
            tenantId:         ret.TenantId,
            creditNoteNumber: creditNoteNumber,
            supplierId:       ret.SupplierId,
            supplierReturnId: ret.Id,
            amount:           ret.TotalValue,
            currency:         ret.Currency,
            notes:            $"Issued for return {ret.ReturnNumber} ({ret.Reason})");

        _db.SupplierCreditNotes.Add(credit);

        // 4. Flip the return state.
        ret.MarkCompleted();
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Supplier return {ReturnNumber} completed for supplier {SupplierId}. Credit note {CreditNote} = Rs {Amount}",
            ret.ReturnNumber, ret.SupplierId, credit.CreditNoteNumber, credit.Amount);

        return credit;
    }

    public async Task CancelAsync(Guid returnId)
    {
        var ret = await _db.SupplierReturns.FirstOrDefaultAsync(r => r.Id == returnId)
            ?? throw new NotFoundException("SupplierReturn", returnId);
        ret.Cancel();
        await _db.SaveChangesAsync();
    }

    public async Task<SupplierCreditApplication> ApplyCreditAsync(
        Guid creditNoteId,
        Guid receivingOrderId,
        decimal amount,
        string? notes)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Applied amount must be positive.");

        var credit = await _db.SupplierCreditNotes
            .FirstOrDefaultAsync(c => c.Id == creditNoteId)
            ?? throw new NotFoundException("SupplierCreditNote", creditNoteId);

        var po = await _db.ReceivingOrders
            .FirstOrDefaultAsync(o => o.Id == receivingOrderId)
            ?? throw new NotFoundException("ReceivingOrder", receivingOrderId);

        if (po.SupplierId != credit.SupplierId)
            throw new BusinessRuleException(
                "Credit note and receiving order belong to different suppliers.");

        // SupplierCreditNote.Apply enforces the "cannot exceed remaining" rule.
        credit.Apply(amount);

        var application = new SupplierCreditApplication(
            tenantId:         credit.TenantId,
            creditNoteId:     credit.Id,
            receivingOrderId: po.Id,
            supplierId:       credit.SupplierId,
            amount:           amount,
            notes:            notes);

        _db.SupplierCreditApplications.Add(application);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Applied Rs {Amount} of credit note {CreditNote} against PO {Po}. Remaining on credit: Rs {Remaining}",
            amount, credit.CreditNoteNumber, po.OrderNumber, credit.Remaining);

        return application;
    }
}
