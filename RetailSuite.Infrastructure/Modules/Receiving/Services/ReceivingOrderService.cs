using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Infrastructure.Modules.Receiving.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Receiving.Services;

/// <summary>
/// Coordinates the receiving-order lifecycle: create draft, edit lines, submit (Draft→Open),
/// record receipts (which call <see cref="InventoryService.ReceiveStockAsync"/>), close, cancel.
/// Inventory side-effects only occur on actual receipts — submission alone doesn't move stock.
/// </summary>
public interface IReceivingOrderService
{
    Task<ReceivingOrder> CreateDraftAsync(Guid tenantId, Guid? supplierId, string? supplierReference, DateTime? expectedDate, string? notes);
    Task AddLineAsync(Guid tenantId, Guid orderId, Guid variantId, int expectedQty, decimal unitCost, string? notes);
    Task RemoveLineAsync(Guid tenantId, Guid orderId, Guid lineId);
    Task SubmitAsync(Guid tenantId, Guid orderId);

    /// <summary>Receive a single line. Moves stock immediately.</summary>
    Task ReceiveLineAsync(Guid tenantId, Guid orderId, Guid lineId, int receivedQty, string? notes);

    /// <summary>Receive multiple lines atomically. Each line moves stock.</summary>
    Task ReceiveBatchAsync(Guid tenantId, Guid orderId, IEnumerable<(Guid LineId, int Qty)> receipts);

    Task CloseAsync(Guid tenantId, Guid orderId);
    Task CancelAsync(Guid tenantId, Guid orderId);

    Task<ReceivingOrder?> GetByIdAsync(Guid tenantId, Guid orderId);
}

public class ReceivingOrderService : IReceivingOrderService
{
    private readonly RetailDbContext _db;
    private readonly InventoryService _inventory;
    private readonly IReceivingOrderNumberGenerator _numbers;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ReceivingOrderService> _logger;

    public ReceivingOrderService(
        RetailDbContext db,
        InventoryService inventory,
        IReceivingOrderNumberGenerator numbers,
        ICurrentUserContext currentUser,
        ILogger<ReceivingOrderService> logger)
    {
        _db          = db;
        _inventory   = inventory;
        _numbers     = numbers;
        _currentUser = currentUser;
        _logger      = logger;
    }

    public async Task<ReceivingOrder> CreateDraftAsync(
        Guid tenantId,
        Guid? supplierId,
        string? supplierReference,
        DateTime? expectedDate,
        string? notes)
    {
        if (supplierId.HasValue)
        {
            var supplierExists = await _db.Suppliers
                .IgnoreQueryFilters()
                .AnyAsync(s => s.Id == supplierId.Value && s.TenantId == tenantId && !s.IsDeleted);
            if (!supplierExists)
                throw new NotFoundException("Supplier", supplierId.Value);
        }

        var number = await _numbers.NextAsync(tenantId);
        var order  = new ReceivingOrder(tenantId, number, supplierId);
        if (!string.IsNullOrWhiteSpace(supplierReference)) order.SetSupplierReference(supplierReference);
        if (expectedDate.HasValue) order.SetExpectedDate(expectedDate);
        if (!string.IsNullOrWhiteSpace(notes)) order.SetNotes(notes);

        _db.ReceivingOrders.Add(order);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Receiving order created: Tenant={TenantId}, Order={Order}, Supplier={SupplierId}",
            tenantId, order.OrderNumber, supplierId);

        return order;
    }

    public async Task AddLineAsync(
        Guid tenantId,
        Guid orderId,
        Guid variantId,
        int expectedQty,
        decimal unitCost,
        string? notes)
    {
        var order = await LoadOrderWithItemsAsync(tenantId, orderId);

        var variant = await _db.ProductVariants
            .IgnoreQueryFilters()
            .Where(v => v.Id == variantId && v.TenantId == tenantId && !v.IsDeleted)
            .Select(v => new { v.Id, v.SKU })
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("ProductVariant", variantId);

        var line = new ReceivingOrderItem(
            tenantId, order.Id, variant.Id, variant.SKU, expectedQty, unitCost, notes);

        order.AddItem(line);
        await _db.SaveChangesAsync();
    }

    public async Task RemoveLineAsync(Guid tenantId, Guid orderId, Guid lineId)
    {
        var order = await LoadOrderWithItemsAsync(tenantId, orderId);
        order.RemoveItem(lineId);
        await _db.SaveChangesAsync();
    }

    public async Task SubmitAsync(Guid tenantId, Guid orderId)
    {
        var order = await LoadOrderWithItemsAsync(tenantId, orderId);
        order.Submit();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Receiving order submitted: {Order}", order.OrderNumber);
    }

    public async Task ReceiveLineAsync(Guid tenantId, Guid orderId, Guid lineId, int receivedQty, string? notes)
    {
        if (receivedQty <= 0)
            throw new BusinessRuleException("Received quantity must be positive.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var order = await LoadOrderWithItemsAsync(tenantId, orderId);
            var line  = order.Items.FirstOrDefault(i => i.Id == lineId)
                ?? throw new NotFoundException("ReceivingOrderItem", lineId);

            // 1. Record receipt on the order (domain validates state + quantities).
            order.RecordReceipt(lineId, receivedQty);
            if (!string.IsNullOrWhiteSpace(notes)) line.SetNotes(notes);

            // 2. Apply inventory side-effect — this also writes an InventoryTransaction.
            await _inventory.ReceiveStockAsync(
                productVariantId: line.ProductVariantId,
                quantity:         receivedQty,
                unitCost:         line.UnitCost,
                referenceId:      $"PO:{order.OrderNumber}");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "Receipt recorded: Order={Order}, Variant={Variant}, Qty={Qty}",
                order.OrderNumber, line.ProductVariantId, receivedQty);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task ReceiveBatchAsync(Guid tenantId, Guid orderId, IEnumerable<(Guid LineId, int Qty)> receipts)
    {
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var order = await LoadOrderWithItemsAsync(tenantId, orderId);

            foreach (var (lineId, qty) in receipts)
            {
                if (qty <= 0) continue;
                var line = order.Items.FirstOrDefault(i => i.Id == lineId)
                    ?? throw new NotFoundException("ReceivingOrderItem", lineId);

                order.RecordReceipt(lineId, qty);

                await _inventory.ReceiveStockAsync(
                    productVariantId: line.ProductVariantId,
                    quantity:         qty,
                    unitCost:         line.UnitCost,
                    referenceId:      $"PO:{order.OrderNumber}");
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task CloseAsync(Guid tenantId, Guid orderId)
    {
        var order = await LoadOrderWithItemsAsync(tenantId, orderId);
        order.Close();
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(Guid tenantId, Guid orderId)
    {
        var order = await LoadOrderWithItemsAsync(tenantId, orderId);
        order.Cancel();
        await _db.SaveChangesAsync();
    }

    public async Task<ReceivingOrder?> GetByIdAsync(Guid tenantId, Guid orderId)
    {
        return await _db.ReceivingOrders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId && !o.IsDeleted);
    }

    private async Task<ReceivingOrder> LoadOrderWithItemsAsync(Guid tenantId, Guid orderId)
    {
        var order = await _db.ReceivingOrders
            .IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId && !o.IsDeleted)
            ?? throw new NotFoundException("ReceivingOrder", orderId);
        return order;
    }
}
