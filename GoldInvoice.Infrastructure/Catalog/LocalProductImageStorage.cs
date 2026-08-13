using GoldInvoice.Application.Catalog;
using GoldInvoice.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Catalog;

internal sealed class LocalProductImageStorage : IProductImageStorage
{
    private readonly string rootPath;

    public LocalProductImageStorage(IOptions<ProductImageStorageOptions> options)
    {
        var configured = options.Value.RootPath;
        rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VendomeJewelry",
                "ProductImages")
            : configured);
        Directory.CreateDirectory(rootPath);
    }

    public async Task<string> SaveAsync(
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var extension = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("The product image type is not supported.", nameof(contentType))
        };
        var identifier = Guid.NewGuid().ToString("N");
        var storageKey = Path.Combine(identifier[..2], identifier + extension).Replace('\\', '/');
        var path = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await stream.WriteAsync(content, cancellationToken);
        return storageKey;
    }

    public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(Resolve(storageKey), cancellationToken);

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
        {
            throw new ArgumentException("The product image storage key is invalid.", nameof(storageKey));
        }

        var candidate = Path.GetFullPath(Path.Combine(rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The product image storage key is invalid.", nameof(storageKey));
        }

        return candidate;
    }
}
