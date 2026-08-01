using System.Data;
using System.Security.Cryptography;
using System.Text;
using GoldInvoice.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoldInvoice.Infrastructure.Persistence;

internal static class PersistenceUtilities
{
    public static async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        GoldInvoiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    public static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public static async Task SaveChangesAsync(
        GoldInvoiceDbContext dbContext,
        CancellationToken cancellationToken)
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

    public static void SetOriginalRowVersion<TEntity>(
        GoldInvoiceDbContext dbContext,
        TEntity entity,
        string value)
        where TEntity : class =>
        dbContext.Entry(entity).Property("RowVersion").OriginalValue = DecodeRowVersion(value);

    public static byte[] DecodeRowVersion(string? value)
    {
        try
        {
            return Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The concurrency token is invalid.", nameof(value), exception);
        }
    }

    public static string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string NormalizeIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length is < 8 or > 128 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The idempotency key must contain 8 to 128 safe characters.", nameof(value));
        }

        return normalized;
    }
}
