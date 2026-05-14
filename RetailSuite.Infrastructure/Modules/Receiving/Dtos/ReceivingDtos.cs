using RetailSuite.Infrastructure.Modules.Receiving.Entities;

namespace RetailSuite.Infrastructure.Modules.Receiving.Dtos;

public record ReceivingOrderResponse(
    Guid     Id,
    string   OrderNumber,
    Guid?    SupplierId,
    string?  SupplierReference,
    string   Status,
    DateTime? ExpectedDate,
    DateTime? SubmittedAt,
    DateTime? ClosedAt,
    string?  Notes,
    decimal  ExpectedTotal,
    decimal  ReceivedTotal,
    string   Currency,
    DateTime CreatedAt,
    IReadOnlyList<ReceivingOrderItemResponse> Items);

public record ReceivingOrderItemResponse(
    Guid     Id,
    Guid     ProductVariantId,
    string   Sku,
    int      ExpectedQuantity,
    int      ReceivedQuantity,
    int      OutstandingQuantity,
    decimal  UnitCost,
    string   Status,
    string?  Notes);

public class CreateReceivingOrderRequest
{
    public Guid?    SupplierId { get; set; }
    public string?  SupplierReference { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string?  Notes { get; set; }
}

public class AddLineRequest
{
    public Guid     ProductVariantId { get; set; }
    public int      ExpectedQuantity { get; set; }
    public decimal  UnitCost { get; set; }
    public string?  Notes { get; set; }
}

public class ReceiveLineRequest
{
    public int ReceivedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class ReceiveBatchRequest
{
    public List<ReceiveBatchLine> Items { get; set; } = new();
}

public class ReceiveBatchLine
{
    public Guid LineId  { get; set; }
    public int  Quantity { get; set; }
}

// ----- Ad-hoc bulk receive (no PO required) ---------------------------

public class AdHocBulkReceiveRequest
{
    /// <summary>Optional free-text reference (e.g. supplier invoice number).</summary>
    public string? ReferenceId { get; set; }

    public List<AdHocBulkReceiveLine> Items { get; set; } = new();
}

public class AdHocBulkReceiveLine
{
    public Guid    ProductVariantId { get; set; }
    public int     Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public static class ReceivingMappers
{
    public static ReceivingOrderResponse ToResponse(this ReceivingOrder o) =>
        new(o.Id, o.OrderNumber, o.SupplierId, o.SupplierReference,
            o.Status.ToString(), o.ExpectedDate, o.SubmittedAt, o.ClosedAt,
            o.Notes, o.ExpectedTotal, o.ReceivedTotal, o.Currency, o.CreatedAt,
            o.Items.Select(i => i.ToResponse()).ToList());

    public static ReceivingOrderItemResponse ToResponse(this ReceivingOrderItem i) =>
        new(i.Id, i.ProductVariantId, i.Sku,
            i.ExpectedQuantity, i.ReceivedQuantity, i.OutstandingQuantity,
            i.UnitCost, i.Status.ToString(), i.Notes);
}
