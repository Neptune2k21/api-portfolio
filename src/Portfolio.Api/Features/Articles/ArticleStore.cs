using Microsoft.Extensions.Options;
using Portfolio.Api.Common;
using Portfolio.Api.Features.Articles.Sources;

namespace Portfolio.Api.Features.Articles;

/// <summary>
/// Index en mémoire du dépôt de contenu. Rechargé à chaque push (<see cref="RefreshAsync"/>, appelé par le webhook)
/// et, au cas où un webhook se perdrait, après <see cref="ArticlesOptions.RefreshInterval"/>.
/// Si la source tombe, l'index précédent continue d'être servi.
/// </summary>
public sealed class ArticleStore(IContentSource source, TimeProvider time, IOptions<ArticlesOptions> options, ILogger<ArticleStore> logger)
{
    // Après un échec, la source n'est pas resollicitée à chaque requête.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly ArticlesOptions _options = options.Value;
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
    private readonly SemaphoreSlim _reload = new(1, 1);
    private volatile Snapshot? _snapshot;

    // Une copie locale se relit à chaque requête : on voit le fichier tel qu'il vient d'être enregistré.
    private TimeSpan RefreshInterval => _options.LocalPath is not { Length: > 0 } ? _options.RefreshInterval : TimeSpan.Zero;

    /// <summary>Ce que le public peut lire aujourd'hui ; en local, brouillons et articles programmés compris.</summary>
    public async Task<IReadOnlyList<Article>> GetVisibleAsync(CancellationToken ct)
    {
        var articles = await GetAllAsync(ct);
        if (_options.IncludeDrafts)
            return articles;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);
        return [.. articles.Where(a => a.IsPublishedOn(today))];
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await _reload.WaitAsync(ct);
        try
        {
            await ReloadAsync(ct);
        }
        finally
        {
            _reload.Release();
        }
    }

    private async Task<IReadOnlyList<Article>> GetAllAsync(CancellationToken ct)
    {
        if (_snapshot is { } current && current.IsFresh(time))
            return current.Articles;

        await _reload.WaitAsync(ct);
        try
        {
            // Une autre requête a peut-être rechargé pendant l'attente.
            if (_snapshot is { } reloaded && reloaded.IsFresh(time))
                return reloaded.Articles;

            return await ReloadAsync(ct);
        }
        catch (UpstreamException e) when (_snapshot is { } stale)
        {
            logger.LogWarning(e, "Dépôt de contenu injoignable, l'index précédent reste servi");
            _snapshot = stale with { NextCheck = time.GetUtcNow() + RetryDelay };
            return stale.Articles;
        }
        finally
        {
            _reload.Release();
        }
    }

    private async Task<IReadOnlyList<Article>> ReloadAsync(CancellationToken ct)
    {
        var articles = ArticleParser.ParseAll(await source.LoadAsync(ct), logger);
        _snapshot = new Snapshot(articles, time.GetUtcNow() + RefreshInterval);
        return articles;
    }

    private sealed record Snapshot(IReadOnlyList<Article> Articles, DateTimeOffset NextCheck)
    {
        public bool IsFresh(TimeProvider time) => time.GetUtcNow() < NextCheck;
    }
}
