using Microsoft.Extensions.Options;
using Portfolio.Api.Common;
using Portfolio.Api.Features.Music.Artwork;
using Portfolio.Api.Features.Music.LastFm;

namespace Portfolio.Api.Features.Music;

public sealed class MusicOptions
{
    public const string Section = "Music";

    /// <summary>Fuseau dans lequel sont lues l'activité d'écoute et les dates des bandes-son.</summary>
    public string TimeZone { get; set; } = "Europe/Paris";
}

/// <summary>
/// Chaque lecture passe par le cache, avec une durée calée sur la vitesse à laquelle la donnée change :
/// Last.fm et Deezer ne sont sollicités qu'une fois par période, quel que soit le trafic.
/// </summary>
public sealed class MusicService(
    ILastFmClient lastFm,
    IArtworkSource artwork,
    CoalescingCache cache,
    TimeProvider time,
    IOptions<MusicOptions> options,
    ILogger<MusicService> logger)
{
    private static readonly TimeSpan ChartTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ArtworkTtl = TimeSpan.FromDays(7);

    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public Task<Scrobble?> GetLatestAsync(CancellationToken ct) =>
        cache.GetOrLoadAsync("music:latest", TimeSpan.FromSeconds(30),
            async t => (await lastFm.GetRecentAsync(1, t)).FirstOrDefault(), ct);

    public Task<IReadOnlyList<Scrobble>> GetRecentAsync(int limit, CancellationToken ct) =>
        cache.GetOrLoadAsync($"music:recent:{limit}", TimeSpan.FromMinutes(1), async t =>
        {
            // Last.fm ajoute le morceau en cours en plus de la limite demandée.
            IReadOnlyList<Scrobble> recent = [.. (await lastFm.GetRecentAsync(limit, t)).Take(limit)];
            return await FillArtworkAsync(recent, s => s.CoverUrl, s => $"track:{s.Artist}:{s.Title}",
                (s, c) => artwork.FindTrackCoverAsync(s.Artist, s.Title, c), (s, url) => s with { CoverUrl = url }, t);
        }, ct);

    public Task<ListenerProfile> GetProfileAsync(CancellationToken ct) =>
        cache.GetOrLoadAsync("music:profile", ChartTtl, lastFm.GetProfileAsync, ct);

    public Task<IReadOnlyList<ArtistPlays>> GetTopArtistsAsync(string period, int limit, CancellationToken ct) =>
        cache.GetOrLoadAsync($"music:top:artists:{period}:{limit}", ChartTtl, async t =>
            await FillArtworkAsync(await lastFm.GetTopArtistsAsync(period, limit, t), a => a.ImageUrl, a => $"artist:{a.Name}",
                (a, c) => artwork.FindArtistImageAsync(a.Name, c), (a, url) => a with { ImageUrl = url }, t), ct);

    public Task<IReadOnlyList<TrackPlays>> GetTopTracksAsync(string period, int limit, CancellationToken ct) =>
        cache.GetOrLoadAsync($"music:top:tracks:{period}:{limit}", ChartTtl, async t =>
            await FillArtworkAsync(await lastFm.GetTopTracksAsync(period, limit, t), s => s.CoverUrl, s => $"track:{s.Artist}:{s.Title}",
                (s, c) => artwork.FindTrackCoverAsync(s.Artist, s.Title, c), (s, url) => s with { CoverUrl = url }, t), ct);

    public Task<IReadOnlyList<AlbumPlays>> GetTopAlbumsAsync(string period, int limit, CancellationToken ct) =>
        cache.GetOrLoadAsync($"music:top:albums:{period}:{limit}", ChartTtl, async t =>
            await FillArtworkAsync(await lastFm.GetTopAlbumsAsync(period, limit, t), a => a.CoverUrl, a => $"album:{a.Artist}:{a.Title}",
                (a, c) => artwork.FindAlbumCoverAsync(a.Artist, a.Title, c), (a, url) => a with { CoverUrl = url }, t), ct);

    public Task<ListeningActivity> GetActivityAsync(int days, CancellationToken ct) =>
        cache.GetOrLoadAsync($"music:activity:{days}", ChartTtl, async t =>
        {
            var to = time.GetUtcNow();
            var from = to.AddDays(-days);
            return ListeningActivity.Build(await lastFm.GetPlayTimesAsync(from, to, t), from, to, _zone);
        }, ct);

    public Task<Soundtrack> GetSoundtrackAsync(DateOnly from, DateOnly to, int limit, CancellationToken ct)
    {
        var start = StartOfDay(from);
        var end = StartOfDay(to.AddDays(1));
        // Une période terminée ne bougera plus.
        var ttl = end <= time.GetUtcNow() ? TimeSpan.FromDays(1) : ChartTtl;

        return cache.GetOrLoadAsync($"music:soundtrack:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{limit}", ttl,
            async t => Soundtrack.Build(from, to, await lastFm.GetArtistChartAsync(start, end, t), limit), ct);
    }

    /// <summary>Complète les visuels manquants. Un visuel introuvable ou une source en panne ne fait jamais échouer la réponse.</summary>
    private async Task<IReadOnlyList<T>> FillArtworkAsync<T>(
        IReadOnlyList<T> items,
        Func<T, string?> current,
        Func<T, string> key,
        Func<T, CancellationToken, Task<string?>> find,
        Func<T, string, T> withUrl,
        CancellationToken ct)
    {
        var urls = await Task.WhenAll(items.Select(item => current(item) is { } url
            ? Task.FromResult<string?>(url)
            : FindArtworkAsync(key(item), c => find(item, c), ct)));

        return [.. items.Select((item, i) => urls[i] is { } url ? withUrl(item, url) : item)];
    }

    private async Task<string?> FindArtworkAsync(string key, Func<CancellationToken, Task<string?>> find, CancellationToken ct)
    {
        try
        {
            return await cache.GetOrLoadAsync($"music:art:{key.ToLowerInvariant()}", ArtworkTtl, find, ct);
        }
        catch (UpstreamException e)
        {
            logger.LogDebug(e, "Visuel indisponible pour {Key}", key);
            return null;
        }
    }

    private DateTimeOffset StartOfDay(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }
}
