using GoldInvoice.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class AccessResolutionCacheTests
{
    [Fact]
    public async Task GetOrLoadAsync_ReusesCachedResolutionWithinLifetime()
    {
        using var memoryCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var cache = new AccessResolutionCache(memoryCache);
        var userId = Guid.NewGuid();
        var loadCount = 0;

        var first = await cache.GetOrLoadAsync(
            userId,
            () => Task.FromResult(Load(ref loadCount)),
            CancellationToken.None);
        var second = await cache.GetOrLoadAsync(
            userId,
            () => Task.FromResult(Load(ref loadCount)),
            CancellationToken.None);

        Assert.Equal(1, loadCount);
        Assert.Equal(first.Roles, second.Roles);
        Assert.Equal(first.Permissions, second.Permissions);
    }

    [Fact]
    public async Task Invalidate_ForcesANewResolve()
    {
        using var memoryCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var cache = new AccessResolutionCache(memoryCache);
        var userId = Guid.NewGuid();
        var loadCount = 0;

        await cache.GetOrLoadAsync(
            userId,
            () => Task.FromResult(Load(ref loadCount)),
            CancellationToken.None);
        cache.Invalidate(userId);
        await cache.GetOrLoadAsync(
            userId,
            () => Task.FromResult(Load(ref loadCount)),
            CancellationToken.None);

        Assert.Equal(2, loadCount);
    }

    private static ResolvedAccess Load(ref int loadCount)
    {
        loadCount++;
        return new ResolvedAccess(["Customer"], ["Orders.Read"]);
    }
}