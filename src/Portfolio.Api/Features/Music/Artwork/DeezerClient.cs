using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Polly;
using Portfolio.Api.Common;

namespace Portfolio.Api.Features.Music.Artwork;

/// <summary>Recherche publique Deezer (sans clé). Sa recherche est floue : on ne garde un résultat que si l'artiste correspond.</summary>
internal sealed class DeezerClient(HttpClient http) : IArtworkSource
{
    public const string BaseUrl = "https://api.deezer.com/";

    private const string Service = "Deezer";
    private const int Candidates = 5;

    // Deezer limite à 50 requêtes par tranche de 5 secondes.
    private static readonly SemaphoreSlim Throttle = new(4);

    public async Task<string?> FindArtistImageAsync(string artist, CancellationToken ct)
    {
        var artists = await SearchAsync("search/artist", artist, DeezerJson.Default.ArtistSearch, ct);
        return Usable(artists.FirstOrDefault(a => SameArtist(a.Name, artist))?.PictureXl);
    }

    public async Task<string?> FindTrackCoverAsync(string artist, string title, CancellationToken ct)
    {
        var tracks = await SearchAsync("search/track", $"{artist} {title}", DeezerJson.Default.TrackSearch, ct);
        return Usable(tracks.FirstOrDefault(t => SameArtist(t.Artist.Name, artist))?.Album.CoverXl);
    }

    public async Task<string?> FindAlbumCoverAsync(string artist, string album, CancellationToken ct)
    {
        var albums = await SearchAsync("search/album", $"{artist} {album}", DeezerJson.Default.AlbumSearch, ct);
        return Usable(albums.FirstOrDefault(a => SameArtist(a.Artist.Name, artist))?.CoverXl);
    }

    private async Task<IReadOnlyList<T>> SearchAsync<T>(string path, string query, JsonTypeInfo<DeezerSearch<T>> type, CancellationToken ct)
    {
        await Throttle.WaitAsync(ct);
        try
        {
            var result = await http.GetFromJsonAsync($"{path}?q={Uri.EscapeDataString(query)}&limit={Candidates}", type, ct)
                ?? throw new UpstreamException(Service, "Réponse vide.");
            // Deezer signale ses erreurs (quota dépassé…) dans un 200.
            if (result.Error is { } error)
                throw new UpstreamException(Service, error.Message);
            return result.Data ?? [];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or ExecutionRejectedException)
        {
            throw new UpstreamException(Service, e.Message, e);
        }
        finally
        {
            Throttle.Release();
        }
    }

    // « bello&dallas » et « Bello&Dallas », « WeRenoi » et « Werenoi » désignent le même artiste.
    internal static bool SameArtist(string a, string b) => Key(a) == Key(b);

    private static string Key(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    // Sans visuel, Deezer renvoie une URL au hash vide (…/images/artist//1000x1000…).
    private static string? Usable(string? url) => url is null || url.Contains("//1000x1000", StringComparison.Ordinal) ? null : url;
}
