/// <summary>In-memory shopping cart — singleton scoped to the browser session via Blazor Server circuit.</summary>
public class CartService
{
    private readonly List<CartItem> _items = new();

    public IReadOnlyList<CartItem> Items => _items;

    public int TotalCount => _items.Sum(i => i.Quantity);
    public decimal TotalAmount => _items.Sum(i => i.LineTotal);

    public event Action? OnChange;

    public void AddItem(CartItem item)
    {
        var existing = _items.FirstOrDefault(i => i.VariantId == item.VariantId);
        if (existing != null)
            existing.Quantity += item.Quantity;
        else
            _items.Add(item);

        Notify();
    }

    public void RemoveItem(Guid variantId)
    {
        _items.RemoveAll(i => i.VariantId == variantId);
        Notify();
    }

    public void UpdateQuantity(Guid variantId, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.VariantId == variantId);
        if (item == null) return;

        if (quantity <= 0)
            _items.Remove(item);
        else
            item.Quantity = quantity;

        Notify();
    }

    public void Clear()
    {
        _items.Clear();
        Notify();
    }

    private void Notify() => OnChange?.Invoke();
}

public class CartItem
{
    public Guid    VariantId   { get; set; }
    public Guid    ProductId   { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public string  SKU         { get; set; } = string.Empty;
    public string? ImageUrl    { get; set; }
    public decimal UnitPrice   { get; set; }
    public int     Quantity    { get; set; }
    public decimal LineTotal   => UnitPrice * Quantity;
}
