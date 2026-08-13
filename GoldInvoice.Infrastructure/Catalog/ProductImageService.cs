using GoldInvoice.Application.Catalog;
using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Catalog;

internal sealed class ProductImageService(
    GoldInvoiceDbContext dbContext,
    IProductImageStorage storage) : IProductImageService
{
    private const int MaximumImageBytes = 5 * 1024 * 1024;

    public async Task<ProductImageInfo> SetPrimaryImageAsync(
        Guid productId,
        SetPrimaryProductImageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Content.Length is < 1 or > MaximumImageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(command.Content));
        }

        ValidateImage(command.Content, command.ContentType);
        if (!await dbContext.Products.AnyAsync(
                product => product.Id == productId && product.IsActive,
                cancellationToken))
        {
            throw new ApplicationResourceNotFoundException();
        }

        var newStorageKey = await storage.SaveAsync(
            command.Content,
            command.ContentType,
            cancellationToken);
        string? previousStorageKey = null;
        try
        {
            var image = await dbContext.ProductImages.SingleOrDefaultAsync(
                candidate => candidate.ProductId == productId &&
                    candidate.ProductVariantId == null &&
                    candidate.IsPrimary,
                cancellationToken);
            if (image is null)
            {
                image = new ProductImage(productId, newStorageKey, command.ContentType);
                image.Configure(null, command.AltText, sortOrder: 0, isPrimary: true);
                dbContext.ProductImages.Add(image);
            }
            else
            {
                previousStorageKey = image.StorageKey;
                image.ReplaceContent(newStorageKey, command.ContentType);
                image.Configure(null, command.AltText, sortOrder: 0, isPrimary: true);
            }

            await SaveChangesAsync(cancellationToken);
            if (previousStorageKey is not null && previousStorageKey != newStorageKey)
            {
                await TryDeleteAsync(previousStorageKey, cancellationToken);
            }

            return Map(image);
        }
        catch
        {
            await TryDeleteAsync(newStorageKey, cancellationToken);
            throw;
        }
    }

    public async Task<ProductImageContentInfo> GetContentAsync(
        Guid productId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var image = await dbContext.ProductImages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == imageId && candidate.ProductId == productId,
                cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        try
        {
            return new ProductImageContentInfo(
                await storage.ReadAsync(image.StorageKey, cancellationToken),
                image.ContentType);
        }
        catch (FileNotFoundException)
        {
            throw new ApplicationResourceNotFoundException();
        }
        catch (DirectoryNotFoundException)
        {
            throw new ApplicationResourceNotFoundException();
        }
    }

    private static void ValidateImage(byte[] content, string contentType)
    {
        var valid = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => content.Length >= 3 && content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff,
            "image/png" => content.Length >= 8 &&
                content.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/webp" => content.Length >= 12 &&
                content.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("The uploaded product image is invalid or unsupported.", nameof(content));
        }
    }

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

    private async Task TryDeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
        }
        catch (IOException)
        {
            // A harmless orphan can be cleaned later; metadata remains authoritative.
        }
    }

    private static ProductImageInfo Map(ProductImage image) => new(
        image.Id,
        image.ProductId,
        image.ProductVariantId,
        image.ContentType,
        image.AltText,
        image.SortOrder,
        image.IsPrimary,
        Convert.ToBase64String(image.RowVersion));
}
