using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Portfolio.Api.Common;

/// <summary>
/// Cache mémoire où les requêtes simultanées sur une même clé absente partagent un seul chargement :
/// une rafale de visiteurs ne multiplie jamais les appels à l'API d'en face.
/// </summary>
public sealed class CoalescingCache(IMemoryCache cache)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _loads = new();

    public async Task<T> GetOrLoadAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> load, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out T? hit))
            return hit!;

        var shared = _loads.GetOrAdd(key, _ => new Lazy<Task<object?>>(() => LoadAndStoreAsync(key, ttl, load)));
        return (T)(await shared.Value.WaitAsync(ct))!;
    }

    private async Task<object?> LoadAndStoreAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> load)
    {
        try
        {
            // Pas le jeton du premier appelant : d'autres attendent peut-être ce même chargement.
            var value = await load(CancellationToken.None);
            cache.Set(key, value, ttl);
            return value;
        }
        finally
        {
            _loads.TryRemove(key, out _);
        }
    }
}
