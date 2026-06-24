using RetailSuite.Shared;

namespace RetailSuite.Modules.Catalog.Entities;

public class Product : TenantEntity
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Long rich description stored as HTML. Rendered with `MarkupString` on the storefront
    /// (the admin editor is responsible for sanitising before save).
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>One-line teaser used on product cards and meta description. Plain text.</summary>
    public string? ShortDescription { get; private set; }

    /// <summary>URL-friendly identifier, unique per tenant. Auto-generated from Name on create.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Foreign key to Brand (optional). Null = "no brand specified".</summary>
    public Guid? BrandId { get; private set; }

    /// <summary>Unit of measure code: PCS, KG, M, L, BOX, etc. Defaults to PCS.</summary>
    public string UnitOfMeasure { get; private set; } = "PCS";

    /// <summary>
    /// JSON array of spec rows, e.g. <c>[{"Material":"Cotton"},{"Care":"Machine wash"}]</c>.
    /// Stored as a single string column so we don't need a sibling table for ad-hoc specs.
    /// </summary>
    public string? Specs { get; private set; }

    /// <summary>Comma-separated free-form tags, e.g. "summer,casual,sale".</summary>
    public string? Tags { get; private set; }

    public bool    IsActive { get; private set; } = true;

    /// <summary>Relative URL of the primary product image, e.g. /uploads/{tenantId}/abc.jpg</summary>
    public string? ImageUrl { get; private set; }

    private readonly List<ProductVariant> _variants = new();
    public IReadOnlyCollection<ProductVariant> Variants => _variants;

    private readonly List<ProductCategory> _categories = new();
    public IReadOnlyCollection<ProductCategory> Categories => _categories;

    private Product() { }

    public Product(string name, string? description, string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name required.", nameof(name));

        Name        = name.Trim();
        Description = description ?? "";
        Slug        = string.IsNullOrWhiteSpace(slug) ? Slugify(Name) : slug.Trim().ToLowerInvariant();
    }

    public void Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name required.");

        Name        = name.Trim();
        Description = description ?? "";
    }

    /// <summary>Overwrite the slug. Caller should ensure uniqueness per tenant.</summary>
    public void SetSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug cannot be empty.");
        Slug = slug.Trim().ToLowerInvariant();
    }

    public void SetShortDescription(string? value) => ShortDescription = value;
    public void SetBrand(Guid? brandId)             => BrandId = brandId;
    public void SetUnitOfMeasure(string uom)        => UnitOfMeasure = string.IsNullOrWhiteSpace(uom) ? "PCS" : uom.Trim().ToUpperInvariant();
    public void SetSpecs(string? specsJson)         => Specs = specsJson;
    public void SetTags(string? tags)               => Tags  = tags;
    public void SetImageUrl(string url)             => ImageUrl = url;
    public void Activate()                          => IsActive = true;
    public void Deactivate()                        => IsActive = false;

    public void AddVariant(ProductVariant variant)
    {
        if (_variants.Any(v => v.SKU == variant.SKU))
            throw new InvalidOperationException("Duplicate SKU.");

        _variants.Add(variant);
    }

    /// <summary>
    /// Convert "Blue Cotton Shirt — Men's" to "blue-cotton-shirt-mens".
    /// Diacritics and non-ASCII are stripped to ASCII letters/digits/hyphen.
    /// </summary>
    public static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "p";
        var sb = new System.Text.StringBuilder(input.Length);
        var prevHyphen = false;
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                prevHyphen = false;
            }
            else if (!prevHyphen && sb.Length > 0)
            {
                sb.Append('-');
                prevHyphen = true;
            }
        }
        var result = sb.ToString().Trim('-');
        return result.Length == 0 ? "p" : result;
    }
}
