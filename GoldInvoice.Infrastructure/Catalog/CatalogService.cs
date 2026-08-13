using GoldInvoice.Application.Catalog;
using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Catalog;

internal sealed class CatalogService(GoldInvoiceDbContext dbContext) : ICatalogService
{
    private const int MaximumPageSize = 100;

    public async Task<IReadOnlyList<ProductCategoryInfo>> GetCategoriesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProductCategories.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return (await query
                .OrderBy(category => category.ParentCategoryId)
                .ThenBy(category => category.DisplayOrder)
                .ThenBy(category => category.Name)
                .ToListAsync(cancellationToken))
            .Select(MapCategory)
            .ToArray();
    }

    public async Task<ProductCategoryInfo> GetCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == categoryId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return MapCategory(category);
    }

    public async Task<ProductCategoryInfo> CreateCategoryAsync(
        CreateProductCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await ValidateCategoryParentAsync(Guid.Empty, command.ParentCategoryId, cancellationToken);

        var category = new ProductCategory(
            command.Name,
            command.Slug,
            command.ParentCategoryId,
            command.DisplayOrder);
        dbContext.ProductCategories.Add(category);
        await SaveChangesAsync(cancellationToken);
        return MapCategory(category);
    }

    public async Task<ProductCategoryInfo> UpdateCategoryAsync(
        Guid categoryId,
        UpdateProductCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var category = await dbContext.ProductCategories.FindAsync([categoryId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(category, command.RowVersion);
        await ValidateCategoryParentAsync(categoryId, command.ParentCategoryId, cancellationToken);
        category.Update(
            command.Name,
            command.Slug,
            command.ParentCategoryId,
            command.DisplayOrder,
            command.IsActive);
        await SaveChangesAsync(cancellationToken);
        return MapCategory(category);
    }

    public async Task DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategories.FindAsync([categoryId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var hasChildren = await dbContext.ProductCategories
            .AnyAsync(item => item.ParentCategoryId == categoryId, cancellationToken);
        var hasProducts = await dbContext.Products
            .IgnoreQueryFilters()
            .AnyAsync(product => product.ProductCategoryId == categoryId, cancellationToken);
        if (hasChildren || hasProducts)
        {
            throw new ApplicationConflictException();
        }

        dbContext.ProductCategories.Remove(category);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<ProductInfo>> GetProductsAsync(
        int page,
        int pageSize,
        Guid? categoryId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        var query = dbContext.Products.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(product => product.IsActive);
        }

        if (categoryId is not null)
        {
            query = query.Where(product => product.ProductCategoryId == categoryId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip(CalculateSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var mapped = await MapProductsAsync(products, includeInactive, cancellationToken);
        return new PagedResult<ProductInfo>(mapped, page, pageSize, totalCount);
    }

    public async Task<ProductInfo> GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return (await MapProductsAsync([product], includeInactiveVariants: true, cancellationToken))[0];
    }

    public async Task<ProductInfo> CreateProductAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await EnsureCategoryExistsAsync(command.ProductCategoryId, cancellationToken);
        var product = new Product(command.Name, command.Slug, command.ProductCategoryId);
        product.Update(
            command.Name,
            command.Slug,
            command.Description,
            command.ProductCategoryId,
            isActive: true);
        dbContext.Products.Add(product);
        await SaveChangesAsync(cancellationToken);
        return (await MapProductsAsync([product], includeInactiveVariants: true, cancellationToken))[0];
    }

    public async Task<ProductInfo> UpdateProductAsync(
        Guid productId,
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var product = await dbContext.Products.FindAsync([productId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        SetOriginalRowVersion(product, command.RowVersion);
        await EnsureCategoryExistsAsync(command.ProductCategoryId, cancellationToken);
        product.Update(
            command.Name,
            command.Slug,
            command.Description,
            command.ProductCategoryId,
            command.IsActive);
        await SaveChangesAsync(cancellationToken);
        return (await MapProductsAsync([product], includeInactiveVariants: true, cancellationToken))[0];
    }

    public async Task<ProductVariantInfo> CreateVariantAsync(
        Guid productId,
        CreateProductVariantCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var productExists = await dbContext.Products.AnyAsync(
            product => product.Id == productId && product.IsActive,
            cancellationToken);
        if (!productExists)
        {
            throw new ApplicationResourceNotFoundException();
        }

        var detailCommand = command.GoldDetail;
        var variant = new ProductVariant(
            productId,
            command.Sku,
            command.Name,
            detailCommand.GrossWeight,
            ToFineness(detailCommand.Karat),
            ToCompatibilityLaborFee(detailCommand),
            fixedPriceRials: null);
        var detail = CreateGoldDetail(variant.Id, detailCommand);
        dbContext.ProductVariants.Add(variant);
        dbContext.GoldProductDetails.Add(detail);
        await SaveChangesAsync(cancellationToken);
        return MapVariant(variant, detail);
    }

    public async Task<ProductVariantInfo> GetVariantAsync(
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var variant = await dbContext.ProductVariants
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == variantId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var detail = await dbContext.GoldProductDetails
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProductVariantId == variantId, cancellationToken);
        return MapVariant(variant, detail);
    }

    public async Task<ProductVariantInfo> UpdateVariantAsync(
        Guid variantId,
        UpdateProductVariantCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var variant = await dbContext.ProductVariants.FindAsync([variantId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var detail = await dbContext.GoldProductDetails
            .SingleOrDefaultAsync(item => item.ProductVariantId == variantId, cancellationToken);
        SetOriginalRowVersion(variant, command.VariantRowVersion);
        if (detail is not null)
        {
            SetOriginalRowVersion(
                detail,
                command.GoldDetailRowVersion ?? throw new ArgumentException(
                    "The gold-detail concurrency token is required.",
                    nameof(command)));
        }

        variant.Update(
            command.Sku,
            command.Name,
            command.GoldDetail.GrossWeight,
            ToFineness(command.GoldDetail.Karat),
            ToCompatibilityLaborFee(command.GoldDetail),
            fixedPriceRials: null,
            command.IsActive);
        if (detail is null)
        {
            detail = CreateGoldDetail(variant.Id, command.GoldDetail);
            dbContext.GoldProductDetails.Add(detail);
        }
        else
        {
            detail.Update(
                command.GoldDetail.Karat,
                command.GoldDetail.GrossWeight,
                command.GoldDetail.NetGoldWeight,
                command.GoldDetail.StoneWeight,
                command.GoldDetail.OtherMaterialWeight,
                command.GoldDetail.ManufacturingWageType,
                command.GoldDetail.ManufacturingWageValue,
                command.GoldDetail.ProfitPercentage,
                command.GoldDetail.TaxPercentage,
                command.GoldDetail.HasStone,
                command.GoldDetail.IsWeightVariable);
        }

        await SaveChangesAsync(cancellationToken);
        return MapVariant(variant, detail);
    }

    private async Task ValidateCategoryParentAsync(
        Guid categoryId,
        Guid? parentCategoryId,
        CancellationToken cancellationToken)
    {
        if (parentCategoryId is null)
        {
            return;
        }

        var currentId = parentCategoryId;
        var visited = new HashSet<Guid>();
        while (currentId is not null)
        {
            if (currentId == categoryId || !visited.Add(currentId.Value))
            {
                throw new ApplicationConflictException();
            }

            currentId = await dbContext.ProductCategories
                .Where(category => category.Id == currentId.Value)
                .Select(category => category.ParentCategoryId)
                .SingleOrDefaultAsync(cancellationToken);
            if (currentId is null && !await dbContext.ProductCategories
                    .AnyAsync(category => category.Id == parentCategoryId, cancellationToken))
            {
                throw new ApplicationResourceNotFoundException();
            }
        }
    }

    private async Task EnsureCategoryExistsAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return;
        }

        if (!await dbContext.ProductCategories.AnyAsync(
                category => category.Id == categoryId && category.IsActive,
                cancellationToken))
        {
            throw new ApplicationResourceNotFoundException();
        }
    }

    private async Task<IReadOnlyList<ProductInfo>> MapProductsAsync(
        IReadOnlyList<Product> products,
        bool includeInactiveVariants,
        CancellationToken cancellationToken)
    {
        var productIds = products.Select(product => product.Id).ToArray();
        var variantsQuery = dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => productIds.Contains(variant.ProductId));
        if (!includeInactiveVariants)
        {
            variantsQuery = variantsQuery.Where(variant => variant.IsActive);
        }

        var variants = await variantsQuery
            .OrderBy(variant => variant.Name)
            .ToListAsync(cancellationToken);
        var variantIds = variants.Select(variant => variant.Id).ToArray();
        var details = await dbContext.GoldProductDetails
            .AsNoTracking()
            .Where(detail => variantIds.Contains(detail.ProductVariantId))
            .ToDictionaryAsync(detail => detail.ProductVariantId, cancellationToken);
        var images = await dbContext.ProductImages
            .AsNoTracking()
            .Where(image => productIds.Contains(image.ProductId))
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .ToListAsync(cancellationToken);

        return products.Select(product => new ProductInfo(
                product.Id,
                product.ProductCategoryId,
                product.Name,
                product.Slug,
                product.Description,
                product.IsActive,
                variants
                    .Where(variant => variant.ProductId == product.Id)
                    .Select(variant => MapVariant(
                        variant,
                        details.TryGetValue(variant.Id, out var detail) ? detail : null))
                    .ToArray(),
                images
                    .Where(image => image.ProductId == product.Id)
                    .Select(MapImage)
                    .ToArray(),
                EncodeRowVersion(product.RowVersion)))
            .ToArray();
    }

    private static GoldProductDetail CreateGoldDetail(Guid variantId, GoldProductDetailCommand detail) =>
        new(
            variantId,
            detail.Karat,
            detail.GrossWeight,
            detail.NetGoldWeight,
            detail.StoneWeight,
            detail.OtherMaterialWeight,
            detail.ManufacturingWageType,
            detail.ManufacturingWageValue,
            detail.ProfitPercentage,
            detail.TaxPercentage,
            detail.HasStone,
            detail.IsWeightVariable);

    private static ProductCategoryInfo MapCategory(ProductCategory category) => new(
        category.Id,
        category.Name,
        category.Slug,
        category.ParentCategoryId,
        category.DisplayOrder,
        category.IsActive,
        EncodeRowVersion(category.RowVersion));

    private static ProductVariantInfo MapVariant(ProductVariant variant, GoldProductDetail? detail) => new(
        variant.Id,
        variant.ProductId,
        variant.Sku,
        variant.Name,
        variant.IsActive,
        detail is null
            ? null
            : new GoldProductDetailInfo(
                detail.Karat,
                detail.GrossWeight,
                detail.NetGoldWeight,
                detail.StoneWeight,
                detail.OtherMaterialWeight,
                detail.ManufacturingWageType,
                detail.ManufacturingWageValue,
                detail.ProfitPercentage,
                detail.TaxPercentage,
                detail.HasStone,
                detail.IsWeightVariable,
                EncodeRowVersion(detail.RowVersion)),
        EncodeRowVersion(variant.RowVersion));

    private static ProductImageInfo MapImage(ProductImage image) => new(
        image.Id,
        image.ProductId,
        image.ProductVariantId,
        image.ContentType,
        image.AltText,
        image.SortOrder,
        image.IsPrimary,
        EncodeRowVersion(image.RowVersion));

    private void SetOriginalRowVersion<TEntity>(TEntity entity, string value)
        where TEntity : class =>
        dbContext.Entry(entity).Property("RowVersion").OriginalValue = DecodeRowVersion(value);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationConcurrencyException();
        }
        catch (DbUpdateException)
        {
            throw new ApplicationConflictException();
        }
    }

    private static int ToFineness(int karat)
    {
        if (!GoldProductDetail.IsSupportedKarat(karat))
        {
            throw new ArgumentOutOfRangeException(nameof(karat));
        }

        return checked((int)decimal.Round(karat * 1000m / 24m, 0, MidpointRounding.AwayFromZero));
    }

    private static long ToCompatibilityLaborFee(GoldProductDetailCommand detail) =>
        detail.ManufacturingWageType == ManufacturingWageType.FixedRials
            ? checked((long)detail.ManufacturingWageValue)
            : 0;

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
    }

    private static int CalculateSkip(int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        return skip <= int.MaxValue
            ? (int)skip
            : throw new ArgumentOutOfRangeException(nameof(page));
    }

    private static string EncodeRowVersion(byte[] value) => Convert.ToBase64String(value);

    private static byte[] DecodeRowVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The concurrency token is invalid.", nameof(value), exception);
        }
    }
}
