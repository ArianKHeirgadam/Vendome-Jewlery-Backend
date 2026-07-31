using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Catalog;

public enum ManufacturingWageType
{
    FixedRials,
    PerGramRials,
    PercentageOfGoldValue
}

public sealed class ProductCategory : AuditableEntity
{
    private ProductCategory()
    {
    }

    public ProductCategory(
        string name,
        string slug,
        Guid? parentCategoryId = null,
        int displayOrder = 0)
    {
        Guard.AgainstNegative(displayOrder, nameof(displayOrder));
        ValidateParent(parentCategoryId);
        Name = Guard.Required(name, nameof(name), 200);
        Slug = Guard.Required(slug, nameof(slug), 200).ToLowerInvariant();
        ParentCategoryId = parentCategoryId;
        DisplayOrder = displayOrder;
    }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public Guid? ParentCategoryId { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public void Update(
        string name,
        string slug,
        Guid? parentCategoryId,
        int displayOrder,
        bool isActive)
    {
        Guard.AgainstNegative(displayOrder, nameof(displayOrder));
        ValidateParent(parentCategoryId);
        Name = Guard.Required(name, nameof(name), 200);
        Slug = Guard.Required(slug, nameof(slug), 200).ToLowerInvariant();
        ParentCategoryId = parentCategoryId;
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }

    private void ValidateParent(Guid? parentCategoryId)
    {
        if (parentCategoryId == Guid.Empty || parentCategoryId == Id)
        {
            throw new ArgumentException("A category cannot be its own parent.", nameof(parentCategoryId));
        }
    }
}

public sealed class Product : SoftDeletableEntity
{
    private Product()
    {
    }

    public Product(string name, string slug, Guid? productCategoryId = null)
    {
        if (productCategoryId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty category identifier is required.", nameof(productCategoryId));
        }

        Name = Guard.Required(name, nameof(name), 200);
        Slug = Guard.Required(slug, nameof(slug), 200).ToLowerInvariant();
        ProductCategoryId = productCategoryId;
    }

    public Guid? ProductCategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public void Update(
        string name,
        string slug,
        string? description,
        Guid? productCategoryId,
        bool isActive)
    {
        if (productCategoryId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty category identifier is required.", nameof(productCategoryId));
        }

        Name = Guard.Required(name, nameof(name), 200);
        Slug = Guard.Required(slug, nameof(slug), 200).ToLowerInvariant();
        Description = Guard.Optional(description, nameof(description), 4000);
        ProductCategoryId = productCategoryId;
        IsActive = isActive;
    }
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

    public void Update(
        string sku,
        string name,
        decimal weightGrams,
        int purity,
        long laborFeeRials,
        long? fixedPriceRials,
        bool isActive)
    {
        Guard.AgainstNonPositive(weightGrams, nameof(weightGrams));
        Guard.AgainstOutOfRange(purity, 1, 1000, nameof(purity));
        Guard.AgainstNegative(laborFeeRials, nameof(laborFeeRials));
        if (fixedPriceRials is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPriceRials));
        }

        Sku = Guard.Required(sku, nameof(sku), 64).ToUpperInvariant();
        Name = Guard.Required(name, nameof(name), 200);
        WeightGrams = weightGrams;
        Purity = purity;
        LaborFeeRials = laborFeeRials;
        FixedPriceRials = fixedPriceRials;
        IsActive = isActive;
    }
}

public sealed class GoldProductDetail : AuditableEntity
{
    private static readonly int[] SupportedKarats = [9, 10, 14, 18, 21, 22, 24];

    private GoldProductDetail()
    {
    }

    public GoldProductDetail(
        Guid productVariantId,
        int karat,
        decimal grossWeight,
        decimal netGoldWeight,
        decimal stoneWeight,
        decimal otherMaterialWeight,
        ManufacturingWageType manufacturingWageType,
        decimal manufacturingWageValue,
        decimal profitPercentage,
        decimal taxPercentage,
        bool hasStone,
        bool isWeightVariable)
    {
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        ProductVariantId = productVariantId;
        SetValues(
            karat,
            grossWeight,
            netGoldWeight,
            stoneWeight,
            otherMaterialWeight,
            manufacturingWageType,
            manufacturingWageValue,
            profitPercentage,
            taxPercentage,
            hasStone,
            isWeightVariable);
    }

    public Guid ProductVariantId { get; private set; }

    public int Karat { get; private set; }

    public decimal GrossWeight { get; private set; }

    public decimal NetGoldWeight { get; private set; }

    public decimal StoneWeight { get; private set; }

    public decimal OtherMaterialWeight { get; private set; }

    public ManufacturingWageType ManufacturingWageType { get; private set; }

    public long? ManufacturingWageAmountRials { get; private set; }

    public decimal? ManufacturingWagePercentage { get; private set; }

    public decimal ManufacturingWageValue =>
        ManufacturingWageAmountRials is long amountRials
            ? amountRials
            : ManufacturingWagePercentage ?? 0m;

    public decimal ProfitPercentage { get; private set; }

    public decimal TaxPercentage { get; private set; }

    public bool HasStone { get; private set; }

    public bool IsWeightVariable { get; private set; }

    public void Update(
        int karat,
        decimal grossWeight,
        decimal netGoldWeight,
        decimal stoneWeight,
        decimal otherMaterialWeight,
        ManufacturingWageType manufacturingWageType,
        decimal manufacturingWageValue,
        decimal profitPercentage,
        decimal taxPercentage,
        bool hasStone,
        bool isWeightVariable) =>
        SetValues(
            karat,
            grossWeight,
            netGoldWeight,
            stoneWeight,
            otherMaterialWeight,
            manufacturingWageType,
            manufacturingWageValue,
            profitPercentage,
            taxPercentage,
            hasStone,
            isWeightVariable);

    public static bool IsSupportedKarat(int karat) => SupportedKarats.Contains(karat);

    private void SetValues(
        int karat,
        decimal grossWeight,
        decimal netGoldWeight,
        decimal stoneWeight,
        decimal otherMaterialWeight,
        ManufacturingWageType manufacturingWageType,
        decimal manufacturingWageValue,
        decimal profitPercentage,
        decimal taxPercentage,
        bool hasStone,
        bool isWeightVariable)
    {
        if (!IsSupportedKarat(karat))
        {
            throw new ArgumentOutOfRangeException(nameof(karat), "The gold karat is not supported.");
        }

        Guard.AgainstNonPositive(grossWeight, nameof(grossWeight));
        Guard.AgainstNonPositive(netGoldWeight, nameof(netGoldWeight));
        Guard.AgainstNegative(stoneWeight, nameof(stoneWeight));
        Guard.AgainstNegative(otherMaterialWeight, nameof(otherMaterialWeight));
        Guard.AgainstPercentage(profitPercentage, nameof(profitPercentage));
        Guard.AgainstPercentage(taxPercentage, nameof(taxPercentage));

        if (netGoldWeight + stoneWeight + otherMaterialWeight > grossWeight)
        {
            throw new ArgumentException("Component weights cannot exceed gross weight.", nameof(grossWeight));
        }

        if (hasStone != (stoneWeight > 0))
        {
            throw new ArgumentException("Stone presence must agree with stone weight.", nameof(hasStone));
        }

        var (wageAmountRials, wagePercentage) = ResolveWageValue(
            manufacturingWageType,
            manufacturingWageValue);

        Karat = karat;
        GrossWeight = grossWeight;
        NetGoldWeight = netGoldWeight;
        StoneWeight = stoneWeight;
        OtherMaterialWeight = otherMaterialWeight;
        ManufacturingWageType = manufacturingWageType;
        ManufacturingWageAmountRials = wageAmountRials;
        ManufacturingWagePercentage = wagePercentage;
        ProfitPercentage = profitPercentage;
        TaxPercentage = taxPercentage;
        HasStone = hasStone;
        IsWeightVariable = isWeightVariable;
    }

    private static (long? AmountRials, decimal? Percentage) ResolveWageValue(
        ManufacturingWageType wageType,
        decimal wageValue)
    {
        Guard.AgainstNegative(wageValue, nameof(wageValue));
        return wageType switch
        {
            ManufacturingWageType.FixedRials or ManufacturingWageType.PerGramRials
                when wageValue == decimal.Truncate(wageValue) && wageValue <= long.MaxValue =>
                (checked((long)wageValue), null),
            ManufacturingWageType.FixedRials or ManufacturingWageType.PerGramRials =>
                throw new ArgumentException("Rial wage values must be whole bigint values.", nameof(wageValue)),
            ManufacturingWageType.PercentageOfGoldValue when wageValue <= 100m =>
                (null, wageValue),
            ManufacturingWageType.PercentageOfGoldValue =>
                throw new ArgumentOutOfRangeException(nameof(wageValue), "A wage percentage cannot exceed 100."),
            _ => throw new ArgumentOutOfRangeException(nameof(wageType))
        };
    }
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
