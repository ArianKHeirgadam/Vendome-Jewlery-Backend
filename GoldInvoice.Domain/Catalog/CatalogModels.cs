using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Catalog;

public sealed class Product : SoftDeletableEntity
{
    private Product()
    {
    }

    public Product(string name, string slug)
    {
        Name = Guard.Required(name, nameof(name), 200);
        Slug = Guard.Required(slug, nameof(slug), 200).ToLowerInvariant();
    }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;
}

public sealed class ProductVariant : SoftDeletableEntity
{
    private ProductVariant()
    {
    }

    public ProductVariant(
        Guid productId,
        string sku,
        string name,
        decimal weightGrams,
        int purity,
        long laborFeeRials,
        long? fixedPriceRials = null)
    {
        Guard.AgainstEmpty(productId, nameof(productId));
        Guard.AgainstNonPositive(weightGrams, nameof(weightGrams));
        Guard.AgainstOutOfRange(purity, 1, 1000, nameof(purity));
        Guard.AgainstNegative(laborFeeRials, nameof(laborFeeRials));
        if (fixedPriceRials is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPriceRials));
        }

        ProductId = productId;
        Sku = Guard.Required(sku, nameof(sku), 64).ToUpperInvariant();
        Name = Guard.Required(name, nameof(name), 200);
        WeightGrams = weightGrams;
        Purity = purity;
        LaborFeeRials = laborFeeRials;
        FixedPriceRials = fixedPriceRials;
    }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public decimal WeightGrams { get; private set; }

    public int Purity { get; private set; }

    public long LaborFeeRials { get; private set; }

    public long? FixedPriceRials { get; private set; }

    public bool IsActive { get; private set; } = true;
}

public sealed class ProductImage : SoftDeletableEntity
{
    private ProductImage()
    {
    }

    public ProductImage(Guid productId, string storageKey, string contentType, int sortOrder = 0)
    {
        Guard.AgainstEmpty(productId, nameof(productId));
        Guard.AgainstNegative(sortOrder, nameof(sortOrder));
        ProductId = productId;
        StorageKey = Guard.Required(storageKey, nameof(storageKey), 500);
        ContentType = Guard.Required(contentType, nameof(contentType), 100).ToLowerInvariant();
        SortOrder = sortOrder;
    }

    public Guid ProductId { get; private set; }

    public Guid? ProductVariantId { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public string? AltText { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsPrimary { get; private set; }
}
