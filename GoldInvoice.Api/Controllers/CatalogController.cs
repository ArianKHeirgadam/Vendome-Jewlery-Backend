using GoldInvoice.Application.Catalog;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Catalog;
using GoldInvoice.Contracts.Common;
using GoldInvoice.Domain.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(64 * 1024)]
[Route("api/v1/catalog")]
public sealed class CatalogController(ICatalogService catalogService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<ProductCategoryResponse>>> GetCategories(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Ok((await catalogService.GetCategoriesAsync(includeInactive, cancellationToken))
            .Select(MapCategory)
            .ToArray());

    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpGet("categories/{categoryId:guid}")]
    public async Task<ActionResult<ProductCategoryResponse>> GetCategory(
        Guid categoryId,
        CancellationToken cancellationToken) =>
        Ok(MapCategory(await catalogService.GetCategoryAsync(categoryId, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPost("categories")]
    public async Task<ActionResult<ProductCategoryResponse>> CreateCategory(
        CreateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await catalogService.CreateCategoryAsync(
            new CreateProductCategoryCommand(
                request.Name,
                request.Slug,
                request.ParentCategoryId,
                request.DisplayOrder),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetCategory),
            new { categoryId = category.Id },
            MapCategory(category));
    }

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPut("categories/{categoryId:guid}")]
    public async Task<ActionResult<ProductCategoryResponse>> UpdateCategory(
        Guid categoryId,
        UpdateProductCategoryRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapCategory(await catalogService.UpdateCategoryAsync(
            categoryId,
            new UpdateProductCategoryCommand(
                request.Name,
                request.Slug,
                request.ParentCategoryId,
                request.DisplayOrder,
                request.IsActive,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpDelete("categories/{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid categoryId, CancellationToken cancellationToken)
    {
        await catalogService.DeleteCategoryAsync(categoryId, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpGet("products")]
    public async Task<ActionResult<PagedResponse<ProductResponse>>> GetProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await catalogService.GetProductsAsync(
            page,
            pageSize,
            categoryId,
            includeInactive,
            cancellationToken);
        return Ok(new PagedResponse<ProductResponse>
        {
            Items = result.Items.Select(MapProduct).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpGet("products/{productId:guid}")]
    public async Task<ActionResult<ProductResponse>> GetProduct(
        Guid productId,
        CancellationToken cancellationToken) =>
        Ok(MapProduct(await catalogService.GetProductAsync(productId, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPost("products")]
    public async Task<ActionResult<ProductResponse>> CreateProduct(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await catalogService.CreateProductAsync(
            new CreateProductCommand(
                request.ProductCategoryId,
                request.Name,
                request.Slug,
                request.Description),
            cancellationToken);
        return CreatedAtAction(nameof(GetProduct), new { productId = product.Id }, MapProduct(product));
    }

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPut("products/{productId:guid}")]
    public async Task<ActionResult<ProductResponse>> UpdateProduct(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapProduct(await catalogService.UpdateProductAsync(
            productId,
            new UpdateProductCommand(
                request.ProductCategoryId,
                request.Name,
                request.Slug,
                request.Description,
                request.IsActive,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPost("products/{productId:guid}/variants")]
    public async Task<ActionResult<ProductVariantResponse>> CreateVariant(
        Guid productId,
        CreateProductVariantRequest request,
        CancellationToken cancellationToken)
    {
        var variant = await catalogService.CreateVariantAsync(
            productId,
            new CreateProductVariantCommand(
                request.Sku,
                request.Name,
                MapGoldDetail(request.GoldDetail)),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetVariant),
            new { variantId = variant.Id },
            MapVariant(variant));
    }

    [Authorize(Policy = SecurityPermissions.ProductsRead)]
    [HttpGet("variants/{variantId:guid}")]
    public async Task<ActionResult<ProductVariantResponse>> GetVariant(
        Guid variantId,
        CancellationToken cancellationToken) =>
        Ok(MapVariant(await catalogService.GetVariantAsync(variantId, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.ProductsManage)]
    [HttpPut("variants/{variantId:guid}")]
    public async Task<ActionResult<ProductVariantResponse>> UpdateVariant(
        Guid variantId,
        UpdateProductVariantRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapVariant(await catalogService.UpdateVariantAsync(
            variantId,
            new UpdateProductVariantCommand(
                request.Sku,
                request.Name,
                request.IsActive,
                MapGoldDetail(request.GoldDetail),
                request.VariantRowVersion,
                request.GoldDetailRowVersion),
            cancellationToken)));

    private static GoldProductDetailCommand MapGoldDetail(GoldProductDetailRequest request) => new(
        request.Karat,
        request.GrossWeight,
        request.NetGoldWeight,
        request.StoneWeight,
        request.OtherMaterialWeight,
        ParseEnum<ManufacturingWageType>(request.ManufacturingWageType),
        request.ManufacturingWageValue,
        request.ProfitPercentage,
        request.TaxPercentage,
        request.HasStone,
        request.IsWeightVariable);

    private static ProductCategoryResponse MapCategory(ProductCategoryInfo category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Slug = category.Slug,
        ParentCategoryId = category.ParentCategoryId,
        DisplayOrder = category.DisplayOrder,
        IsActive = category.IsActive,
        RowVersion = category.RowVersion
    };

    private static ProductResponse MapProduct(ProductInfo product) => new()
    {
        Id = product.Id,
        ProductCategoryId = product.ProductCategoryId,
        Name = product.Name,
        Slug = product.Slug,
        Description = product.Description,
        IsActive = product.IsActive,
        Variants = product.Variants.Select(MapVariant).ToArray(),
        RowVersion = product.RowVersion
    };

    private static ProductVariantResponse MapVariant(ProductVariantInfo variant) => new()
    {
        Id = variant.Id,
        ProductId = variant.ProductId,
        Sku = variant.Sku,
        Name = variant.Name,
        IsActive = variant.IsActive,
        GoldDetail = MapGoldDetail(variant.GoldDetail),
        RowVersion = variant.RowVersion
    };

    private static GoldProductDetailResponse? MapGoldDetail(GoldProductDetailInfo? detail) =>
        detail is null
            ? null
            : new GoldProductDetailResponse
            {
                Karat = detail.Karat,
                GrossWeight = detail.GrossWeight,
                NetGoldWeight = detail.NetGoldWeight,
                StoneWeight = detail.StoneWeight,
                OtherMaterialWeight = detail.OtherMaterialWeight,
                ManufacturingWageType = detail.ManufacturingWageType.ToString(),
                ManufacturingWageValue = detail.ManufacturingWageValue,
                ProfitPercentage = detail.ProfitPercentage,
                TaxPercentage = detail.TaxPercentage,
                HasStone = detail.HasStone,
                IsWeightVariable = detail.IsWeightVariable,
                RowVersion = detail.RowVersion
            };

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException($"'{value}' is not a supported {typeof(TEnum).Name} value.");
}
