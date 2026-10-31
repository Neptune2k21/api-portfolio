using Microsoft.Extensions.Caching.Memory;
using Portfolio.Api.Common;

namespace Portfolio.Api.Tests.Common;

public sealed class CoalescingCacheTests
{
    private readonly CoalescingCache _cache = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Concurrent_misses_share_a_single_load()
    {
        var loads = 0;
        var release = new TaskCompletionSource<string>();

        var callers = Enumerable.Range(0, 10)
            .Select(_ => _cache.GetOrLoadAsync("key", TimeSpan.FromMinutes(1), _ =>
            {
                Interlocked.Increment(ref loads);
                return release.Task;
            }, CancellationToken.None))
            .ToList();
        release.SetResult("value");

        Assert.All(await Task.WhenAll(callers), v => Assert.Equal("value", v));
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task Loaded_value_is_served_from_cache()
    {
        var loads = 0;
        Task<int> Load(CancellationToken _) => Task.FromResult(Interlocked.Increment(ref loads));

        await _cache.GetOrLoadAsync("key", TimeSpan.FromMinutes(1), Load, CancellationToken.None);
        var second = await _cache.GetOrLoadAsync("key", TimeSpan.FromMinutes(1), Load, CancellationToken.None);

        Assert.Equal(1, second);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task Failed_load_is_not_cached()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _cache.GetOrLoadAsync<int>("key", TimeSpan.FromMinutes(1), _ => throw new InvalidOperationException(), CancellationToken.None));

        var retried = await _cache.GetOrLoadAsync("key", TimeSpan.FromMinutes(1), _ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, retried);
    }
}
