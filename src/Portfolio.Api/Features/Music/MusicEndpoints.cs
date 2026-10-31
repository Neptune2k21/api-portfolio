using Microsoft.AspNetCore.Http.HttpResults;
using Portfolio.Api.Common;

namespace Portfolio.Api.Features.Music;

public static class MusicEndpoints
{
    private const int MaxLimit = 50;
    private const int MaxActivityDays = 90;
    private const int MaxSoundtrackDays = 2 * 366;

    public static RouteGroupBuilder MapMusic(this RouteGroupBuilder api)
    {
        var music = api.MapGroup("/music");
        music.MapGet("/latest", GetLatest);
        music.MapGet("/recent", GetRecent);
        music.MapGet("/profile", GetProfile);
        music.MapGet("/top/artists", GetTopArtists);
        music.MapGet("/top/tracks", GetTopTracks);
        music.MapGet("/top/albums", GetTopAlbums);
        music.MapGet("/activity", GetActivity);
        music.MapGet("/soundtrack", GetSoundtrack);
        return api;
    }

    private static async Task<Results<Ok<Scrobble>, NoContent>> GetLatest(MusicService music, CancellationToken ct) =>
        await music.GetLatestAsync(ct) is { } scrobble ? TypedResults.Ok(scrobble) : TypedResults.NoContent();

    private static async Task<Results<Ok<IReadOnlyList<Scrobble>>, ValidationProblem>> GetRecent(MusicService music, CancellationToken ct, int limit = 20) =>
        Validate.Query(LimitRule(limit)) is { } problem ? problem : TypedResults.Ok(await music.GetRecentAsync(limit, ct));

    private static async Task<Ok<ListenerProfile>> GetProfile(MusicService music, CancellationToken ct) =>
        TypedResults.Ok(await music.GetProfileAsync(ct));

    private static async Task<Results<Ok<IReadOnlyList<ArtistPlays>>, ValidationProblem>> GetTopArtists(
        MusicService music, CancellationToken ct, string period = ChartPeriod.Week, int limit = 10) =>
        Validate.Query(PeriodRule(period), LimitRule(limit)) is { } problem ? problem : TypedResults.Ok(await music.GetTopArtistsAsync(period, limit, ct));

    private static async Task<Results<Ok<IReadOnlyList<TrackPlays>>, ValidationProblem>> GetTopTracks(
        MusicService music, CancellationToken ct, string period = ChartPeriod.Week, int limit = 10) =>
        Validate.Query(PeriodRule(period), LimitRule(limit)) is { } problem ? problem : TypedResults.Ok(await music.GetTopTracksAsync(period, limit, ct));

    private static async Task<Results<Ok<IReadOnlyList<AlbumPlays>>, ValidationProblem>> GetTopAlbums(
        MusicService music, CancellationToken ct, string period = ChartPeriod.Week, int limit = 10) =>
        Validate.Query(PeriodRule(period), LimitRule(limit)) is { } problem ? problem : TypedResults.Ok(await music.GetTopAlbumsAsync(period, limit, ct));

    private static async Task<Results<Ok<ListeningActivity>, ValidationProblem>> GetActivity(MusicService music, CancellationToken ct, int days = 28) =>
        Validate.Query((days is >= 1 and <= MaxActivityDays, nameof(days), $"Entre 1 et {MaxActivityDays}.")) is { } problem
            ? problem
            : TypedResults.Ok(await music.GetActivityAsync(days, ct));

    private static async Task<Results<Ok<Soundtrack>, ValidationProblem>> GetSoundtrack(
        MusicService music, DateOnly from, DateOnly to, CancellationToken ct, int limit = 5)
    {
        if (Validate.Query(
                (from <= to, nameof(to), "Doit être postérieure ou égale à from."),
                (to.DayNumber - from.DayNumber <= MaxSoundtrackDays, nameof(to), $"La période ne peut dépasser {MaxSoundtrackDays} jours."),
                LimitRule(limit)) is { } problem)
            return problem;

        return TypedResults.Ok(await music.GetSoundtrackAsync(from, to, limit, ct));
    }

    private static (bool, string, string) PeriodRule(string period) =>
        (ChartPeriod.IsValid(period), nameof(period), $"Valeurs possibles : {string.Join(", ", ChartPeriod.All)}.");

    private static (bool, string, string) LimitRule(int limit) =>
        (limit is >= 1 and <= MaxLimit, nameof(limit), $"Entre 1 et {MaxLimit}.");
}
