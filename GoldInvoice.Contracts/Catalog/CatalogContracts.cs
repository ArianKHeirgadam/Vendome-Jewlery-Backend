using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Catalog;

public sealed class ProductCategoryResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public Guid? ParentCategoryId { get; init; }
    public required int DisplayOrder { get; init; }
    public required bool IsActive { get; init; }
    public required string RowVersion { get; init; }
}

public class CreateProductCategoryRequest
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Slug { get; init; } = string.Empty;

    public Guid? ParentCategoryId { get; init; }

    [Range(0, int.MaxValue)]
    public int DisplayOrder { get; init; }
}

public sealed class UpdateProductCategoryRequest : CreateProductCategoryRequest
{
    public bool IsActive { get; init; } = true;

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class GoldProductDetailRequest
{
    [Range(1, 24)]
    public int Karat { get; init; }

    [Range(typeof(decimal), "0.001", "999999999999999.999")]
    public decimal GrossWeight { get; init; }

    [Range(typeof(decimal), "0.001", "999999999999999.999")]
    public decimal NetGoldWeight { get; init; }

    [Range(typeof(decimal), "0", "999999999999999.999")]
    public decimal StoneWeight { get; init; }

    [Range(typeof(decimal), "0", "999999999999999.999")]
    public decimal OtherMaterialWeight { get; init; }

    [Required]
    public string ManufacturingWageType { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal ManufacturingWageValue { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal ProfitPercentage { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal TaxPercentage { get; init; }

    public bool HasStone { get; init; }

    public bool IsWeightVariable { get; init; }
}

public sealed class GoldProductDetailResponse
{
    public required int Karat { get; init; }
    public required decimal GrossWeight { get; init; }
    public required decimal NetGoldWeight { get; init; }
    public required decimal StoneWeight { get; init; }
    public required decimal OtherMaterialWeight { get; init; }
    public required string ManufacturingWageType { get; init; }
    public required decimal ManufacturingWageValue { get; init; }
    public required decimal ProfitPercentage { get; init; }
    public required decimal TaxPercentage { get; init; }
    public required bool HasStone { get; init; }
    public required bool IsWeightVariable { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class ProductVariantResponse
{
    public required Guid Id { get; init; }
    public required Guid ProductId { get; init; }
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
    public GoldProductDetailResponse? GoldDetail { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class ProductResponse
{
    public required Guid Id { get; init; }
    public Guid? ProductCategoryId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string? Description { get; init; }
    public required bool IsActive { get; init; }
    public required IReadOnlyList<ProductVariantResponse> Variants { get; init; }
    public required IReadOnlyList<ProductImageResponse> Images { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class ProductImageResponse
{
    public required Guid Id { get; init; }
    public required Guid ProductId { get; init; }
    public Guid? ProductVariantId { get; init; }
    public required string ContentType { get; init; }
    public string? AltText { get; init; }
    public required int SortOrder { get; init; }
    public required bool IsPrimary { get; init; }
    public required string RowVersion { get; init; }
}

public class CreateProductRequest
{
    public Guid? ProductCategoryId { get; init; }

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Slug { get; init; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; init; }
}

public sealed class UpdateProductRequest : CreateProductRequest
{
    public bool IsActive { get; init; } = true;

    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public class CreateProductVariantRequest
{
    [Required, StringLength(64)]
    public string Sku { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public GoldProductDetailRequest GoldDetail { get; init; } = new();
}

public sealed class UpdateProductVariantRequest : CreateProductVariantRequest
{
    public bool IsActive { get; init; } = true;

    [Required]
    public string VariantRowVersion { get; init; } = string.Empty;

    public string? GoldDetailRowVersion { get; init; }
}
