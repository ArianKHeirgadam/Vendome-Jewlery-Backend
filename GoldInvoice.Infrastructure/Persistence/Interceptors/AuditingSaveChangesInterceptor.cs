using GoldInvoice.Domain.Common;
using GoldInvoice.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoldInvoice.Infrastructure.Persistence.Interceptors;

public sealed class AuditingSaveChangesInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyPolicies(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyPolicies(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void ApplyPolicies(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            ApplyDeletePolicy(entry, now);
            ApplyAppendOnlyPolicy(entry);
            ApplyAudit(entry, now);
            SetServerOwnedTimestamps(entry, now);
            NormalizeDateTimes(entry);
        }
    }

    private static void ApplyDeletePolicy(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.State != EntityState.Deleted)
        {
            return;
        }

        if (entry.Entity is ISoftDeletableEntity)
        {
            entry.State = EntityState.Unchanged;
            SetModified(entry, nameof(ISoftDeletableEntity.IsDeleted), true);
            SetModified(entry, nameof(ISoftDeletableEntity.DeletedAt), now);
            SetModified(entry, nameof(ISoftDeletableEntity.DeletedBy), null);
            return;
        }

        if (entry.Entity is IProtectedFromHardDelete or IAppendOnlyEntity)
        {
            throw new InvalidOperationException($"Hard deletion of {entry.Metadata.ClrType.Name} is not allowed.");
        }
    }

    private static void SetModified(EntityEntry entry, string propertyName, object? value)
    {
        var property = entry.Property(propertyName);
        property.CurrentValue = value;
        property.IsModified = true;
    }

    private static void ApplyAppendOnlyPolicy(EntityEntry entry)
    {
        if (entry.Entity is IAppendOnlyEntity && entry.State == EntityState.Modified)
        {
            throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} is append-only.");
        }
    }

    private static void ApplyAudit(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is not IAuditableEntity)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(IAuditableEntity.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(IAuditableEntity.CreatedBy)).CurrentValue = null;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
            entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
        }

        if (entry.State is EntityState.Added or EntityState.Modified)
        {
            entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(IAuditableEntity.UpdatedBy)).CurrentValue = null;
        }
    }

    private static void SetServerOwnedTimestamps(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.State == EntityState.Added && entry.Entity is RolePermission)
        {
            entry.Property(nameof(RolePermission.GrantedAt)).CurrentValue = now;
        }

        if (entry.State == EntityState.Added && entry.Entity is UserSession)
        {
            entry.Property(nameof(UserSession.LastSeenAt)).CurrentValue = now;
        }
    }

    private static void NormalizeDateTimes(EntityEntry entry)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified))
        {
            return;
        }

        foreach (var property in entry.Properties)
        {
            if (property.CurrentValue is DateTimeOffset value)
            {
                property.CurrentValue = value.ToUniversalTime();
            }
        }
    }
}
