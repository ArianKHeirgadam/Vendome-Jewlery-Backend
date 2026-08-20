using Microsoft.Extensions.Caching.Memory;

namespace GoldInvoice.Infrastructure.Security;

/// <summary>
/// In-memory cache for a user's resolved roles and permissions.
///
/// The access-token validator runs once per authenticated request; resolving
/// roles and permissions means two extra round trips to the database (the
/// UserRoles -> Roles and UserRoles -> RolePermissions -> Permissions joins).
/// Caching the resolution for a short window removes those two queries from
/// nearly every request while keeping revocations safe: the per-request
/// session check (user row + session row) is never cached, and role or
/// permission mutations invalidate the entry inline.
/// </summary>
internal sealed class AccessResolutionCache(IMemoryCache cache)
{
    private const string KeyPrefix = "access-resolution:";
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public Task<ResolvedAccess> GetOrLoadAsync(
        Guid userId,
        Func<Task<ResolvedAccess>> loader,
        CancellationToken cancellationToken)
    {
        var key = KeyPrefix + userId.ToString("N");
        if (cache.TryGetValue<ResolvedAccess>(key, out var cached))
        {
            return Task.FromResult(cached!);
        }

        return LoadAsync(key, loader, cancellationToken);
    }

    private async Task<ResolvedAccess> LoadAsync(
        string key,
        Func<Task<ResolvedAccess>> loader,
        CancellationToken cancellationToken)
    {
        var loaded = await loader();
        cache.Set(key, loaded, Lifetime);
        cancellationToken.ThrowIfCancellationRequested();
        return loaded;
    }

    public void Invalidate(Guid userId) => cache.Remove(KeyPrefix + userId.ToString("N"));
}