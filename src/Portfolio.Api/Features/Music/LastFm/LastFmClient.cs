using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using Polly;
using Portfolio.Api.Common;

namespace Portfolio.Api.Features.Music.LastFm;

public sealed class LastFmOptions
{
    public const string Section = "LastFm";

    public string ApiKey { get; set; } = "";

    public string User { get; set; } = "";
}

internal sealed class LastFmClient(HttpClient http, IOptions<LastFmOptions> options) : ILastFmClient
{
    public const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";

    private const string Service = "Last.fm";
    private const int PageSize = 200;
    private const int MaxPages = 25;
    // Last.fm demande de rester sous 5 requêtes par seconde.
    private const int ParallelPages = 4;
    // Image renvoyée par Last.fm quand il n'a pas de pochette.
    private const string PlaceholderImage = "2a96cbd8b46e442fc41c2b86b821562f";

    private readonly LastFmOptions _options = options.Value;

    public async Task<IReadOnlyList<Scrobble>> GetRecentAsync(int limit, CancellationToken ct)
    {
        var response = await GetAsync("user.getrecenttracks", LastFmJson.Default.RecentTracksResponse, ct, ("limit", Num(limit)));
        return [.. (response.RecentTracks.Track ?? []).Select(ToScrobble)];
    }

    public async Task<IReadOnlyList<DateTimeOffset>> GetPlayTimesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var first = await GetRecentPageAsync(from, to, page: 1, ct);
        var pages = new RecentTracksPage[Math.Clamp(first.Attr.TotalPages, 1, MaxPages)];
        pages[0] = first;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, pages.Length - 1),
            new ParallelOptions { MaxDegreeOfParallelism = ParallelPages, CancellationToken = ct },
            async (index, token) => pages[index] = await GetRecentPageAsync(from, to, index + 1, token));

        // Le morceau en cours s'invite même dans une période passée, mais sans date : on l'écarte.
        return [.. pages
            .SelectMany(p => p.Track ?? [])
            .Select(t => t.Date)
            .OfType<PlayDate>()
            .Select(d => DateTimeOffset.FromUnixTimeSeconds(d.Uts))];
    }

    public async Task<ListenerProfile> GetProfileAsync(CancellationToken ct)
    {
        var user = (await GetAsync("user.getinfo", LastFmJson.Default.UserInfoResponse, ct)).User;
        return new ListenerProfile(user.Playcount, user.ArtistCount, user.TrackCount, user.AlbumCount,
            DateTimeOffset.FromUnixTimeSeconds(user.Registered.Unixtime));
    }

    public async Task<IReadOnlyList<ArtistPlays>> GetTopArtistsAsync(string period, int limit, CancellationToken ct)
    {
        var response = await GetAsync("user.gettopartists", LastFmJson.Default.TopArtistsResponse, ct, ("period", period), ("limit", Num(limit)));
        return ToArtists(response.TopArtists);
    }

    public async Task<IReadOnlyList<TrackPlays>> GetTopTracksAsync(string period, int limit, CancellationToken ct)
    {
        var response = await GetAsync("user.gettoptracks", LastFmJson.Default.TopTracksResponse, ct, ("period", period), ("limit", Num(limit)));
        return [.. (response.TopTracks.Track ?? []).Select(t => new TrackPlays(t.Name, t.Artist.Name, t.Playcount, t.Url, Cover(t.Image)))];
    }

    public async Task<IReadOnlyList<AlbumPlays>> GetTopAlbumsAsync(string period, int limit, CancellationToken ct)
    {
        var response = await GetAsync("user.gettopalbums", LastFmJson.Default.TopAlbumsResponse, ct, ("period", period), ("limit", Num(limit)));
        return [.. (response.TopAlbums.Album ?? []).Select(a => new AlbumPlays(a.Name, a.Artist.Name, a.Playcount, a.Url, Cover(a.Image)))];
    }

    public async Task<IReadOnlyList<ArtistPlays>> GetArtistChartAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var response = await GetAsync("user.getweeklyartistchart", LastFmJson.Default.ArtistChartResponse, ct,
            ("from", Num(from.ToUnixTimeSeconds())), ("to", Num(to.ToUnixTimeSeconds())));
        return ToArtists(response.WeeklyArtistChart);
    }

    private async Task<RecentTracksPage> GetRecentPageAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken ct)
    {
        var response = await GetAsync("user.getrecenttracks", LastFmJson.Default.RecentTracksResponse, ct,
            ("from", Num(from.ToUnixTimeSeconds())), ("to", Num(to.ToUnixTimeSeconds())), ("limit", Num(PageSize)), ("page", Num(page)));
        return response.RecentTracks;
    }

    private async Task<T> GetAsync<T>(string method, JsonTypeInfo<T> type, CancellationToken ct, params (string Name, string Value)[] args)
    {
        var query = new QueryBuilder { { "method", method }, { "user", _options.User }, { "api_key", _options.ApiKey }, { "format", "json" } };
        foreach (var (name, value) in args)
            query.Add(name, value);

        try
        {
            using var response = await http.GetAsync(query.ToString(), HttpCompletionOption.ResponseHeadersRead, ct);
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await JsonSerializer.DeserializeAsync(body, LastFmJson.Default.ErrorResponse, ct);
                throw new UpstreamException(Service, error?.Message ?? $"HTTP {(int)response.StatusCode}");
            }

            return await JsonSerializer.DeserializeAsync(body, type, ct) ?? throw new UpstreamException(Service, "Réponse vide.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or ExecutionRejectedException)
        {
            throw new UpstreamException(Service, e.Message, e);
        }
    }

    private static Scrobble ToScrobble(RecentTrack t) =>
        new(t.Name, t.Artist.Text, t.Album.Text, Cover(t.Image), t.Url, t.Date is { } d ? DateTimeOffset.FromUnixTimeSeconds(d.Uts) : null);

    private static List<ArtistPlays> ToArtists(ArtistList list) =>
        [.. (list.Artist ?? []).Select(a => new ArtistPlays(a.Name, a.Playcount, a.Url))];

    // La dernière image de la liste est la plus grande.
    private static string? Cover(IReadOnlyList<Image>? images) =>
        images?.LastOrDefault(i => i.Url.Length > 0)?.Url is { } url && !url.Contains(PlaceholderImage, StringComparison.Ordinal) ? url : null;

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
}
