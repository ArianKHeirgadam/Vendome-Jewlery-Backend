using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Catalog;

namespace GoldInvoice.Application.Catalog;

public sealed record ProductCategoryInfo(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentCategoryId,
    int DisplayOrder,
    bool IsActive,
    string RowVersion);

public sealed record GoldProductDetailInfo(
    int Karat,
    decimal GrossWeight,
    decimal NetGoldWeight,
    decimal StoneWeight,
    decimal OtherMaterialWeight,
    ManufacturingWageType ManufacturingWageType,
    decimal ManufacturingWageValue,
    decimal ProfitPercentage,
    decimal TaxPercentage,
    bool HasStone,
    bool IsWeightVariable,
    string RowVersion);

public sealed record ProductVariantInfo(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    bool IsActive,
    GoldProductDetailInfo? GoldDetail,
    string RowVersion);

public sealed record ProductInfo(
    Guid Id,
    Guid? ProductCategoryId,
    string Name,
    string Slug,
    string? Description,
    bool IsActive,
    IReadOnlyList<ProductVariantInfo> Variants,
    string RowVersion);

public sealed record CreateProductCategoryCommand(
    string Name,
    string Slug,
    Guid? ParentCategoryId,
    int DisplayOrder);

public sealed record UpdateProductCategoryCommand(
    string Name,
    string Slug,
    Guid? ParentCategoryId,
    int DisplayOrder,
    bool IsActive,
    string RowVersion);

public sealed record CreateProductCommand(
    Guid? ProductCategoryId,
    string Name,
    string Slug,
    string? Description);

public sealed record UpdateProductCommand(
    Guid? ProductCategoryId,
    string Name,
    string Slug,
    string? Description,
    bool IsActive,
    string RowVersion);

public sealed record GoldProductDetailCommand(
    int Karat,
    decimal GrossWeight,
    decimal NetGoldWeight,
    decimal StoneWeight,
    decimal OtherMaterialWeight,
    ManufacturingWageType ManufacturingWageType,
    decimal ManufacturingWageValue,
    decimal ProfitPercentage,
    decimal TaxPercentage,
    bool HasStone,
    bool IsWeightVariable);

public sealed record CreateProductVariantCommand(
    string Sku,
    string Name,
    GoldProductDetailCommand GoldDetail);

public sealed record UpdateProductVariantCommand(
    string Sku,
    string Name,
    bool IsActive,
    GoldProductDetailCommand GoldDetail,
    string VariantRowVersion,
    string? GoldDetailRowVersion);

public interface ICatalogService
{
    Task<IReadOnlyList<ProductCategoryInfo>> GetCategoriesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<ProductCategoryInfo> GetCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken);

    Task<ProductCategoryInfo> CreateCategoryAsync(
        CreateProductCategoryCommand command,
        CancellationToken cancellationToken);

    Task<ProductCategoryInfo> UpdateCategoryAsync(
        Guid categoryId,
        UpdateProductCategoryCommand command,
        CancellationToken cancellationToken);

    Task DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<PagedResult<ProductInfo>> GetProductsAsync(
        int page,
        int pageSize,
        Guid? categoryId,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<ProductInfo> GetProductAsync(Guid productId, CancellationToken cancellationToken);

    Task<ProductInfo> CreateProductAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken);

    Task<ProductInfo> UpdateProductAsync(
        Guid productId,
        UpdateProductCommand command,
        CancellationToken cancellationToken);

    Task<ProductVariantInfo> CreateVariantAsync(
        Guid productId,
        CreateProductVariantCommand command,
        CancellationToken cancellationToken);

    Task<ProductVariantInfo> GetVariantAsync(
        Guid variantId,
        CancellationToken cancellationToken);

    Task<ProductVariantInfo> UpdateVariantAsync(
        Guid variantId,
        UpdateProductVariantCommand command,
        CancellationToken cancellationToken);
}
