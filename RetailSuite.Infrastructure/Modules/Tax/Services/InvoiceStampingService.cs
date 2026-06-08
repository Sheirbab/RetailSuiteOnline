using Microsoft.EntityFrameworkCore;
using RetailSuite.Modules.Orders.Entities;

namespace RetailSuite.Infrastructure.Modules.Tax.Services;

/// <summary>
/// Stamps an Order with an FBR-compliant invoice number and frozen seller-identity snapshot.
/// The sale flows (POS SaleService, online ShopController) call this immediately before /
/// after marking the order Completed, so every completed order has a printable invoice.
///
/// Idempotent — calling twice is a no-op.
/// </summary>
public interface IInvoiceStampingService
{
    /// <summary>Apply the invoice stamp to an order that's about to be (or has just been) completed.</summary>
    Task StampAsync(Order order);
}

public class InvoiceStampingService : IInvoiceStampingService
{
    private readonly RetailDbContext _db;
    private readonly ISalesInvoiceNumberGenerator _numbers;

    public InvoiceStampingService(RetailDbContext db, ISalesInvoiceNumberGenerator numbers)
    {
        _db      = db;
        _numbers = numbers;
    }

    public async Task StampAsync(Order order)
    {
        if (order == null) throw new ArgumentNullException(nameof(order));
        if (!string.IsNullOrEmpty(order.InvoiceNumber)) return; // idempotent

        var settings = await _db.TaxSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == order.TenantId);

        var issuedAt = DateTime.UtcNow;
        var invoiceNumber = await _numbers.NextAsync(order.TenantId, issuedAt);

        order.StampInvoice(
            invoiceNumber:      invoiceNumber,
            sellerNtn:          settings?.Ntn,
            sellerStrn:         settings?.Strn,
            sellerBusinessName: settings?.BusinessNameAsRegistered,
            sellerAddress:      settings?.RegisteredAddress);
    }
}
