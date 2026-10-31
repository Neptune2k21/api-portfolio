namespace Portfolio.Api.Features.Music;

/// <summary>Une écoute. <see cref="PlayedAt"/> est null tant que le morceau est en cours.</summary>
public sealed record Scrobble(string Title, string Artist, string Album, string? CoverUrl, string Url, DateTimeOffset? PlayedAt)
{
    public bool NowPlaying => PlayedAt is null;
}

public sealed record ArtistPlays(string Name, int Plays, string Url, string? ImageUrl = null);

public sealed record TrackPlays(string Title, string Artist, int Plays, string Url, string? CoverUrl);

public sealed record AlbumPlays(string Title, string Artist, int Plays, string Url, string? CoverUrl);

/// <summary>Compteurs du compte : écoutes, et nombre d'artistes, titres et albums différents.</summary>
public sealed record ListenerProfile(long Scrobbles, int Artists, int Tracks, int Albums, DateTimeOffset ListeningSince);

/// <summary>Périodes acceptées par les classements Last.fm.</summary>
public static class ChartPeriod
{
    public const string Week = "7day";

    public static readonly IReadOnlyList<string> All = ["7day", "1month", "3month", "6month", "12month", "overall"];

    public static bool IsValid(string period) => All.Contains(period);
}

public sealed record DailyPlays(DateOnly Date, int Plays);

/// <summary>
/// Écoutes des derniers jours, jour par jour (<see cref="Daily"/>, jours vides compris)
/// et par jour de la semaine × heure locale (<see cref="Grid"/>, lundi = 0).
/// </summary>
public sealed record ListeningActivity(
    DateTimeOffset From,
    DateTimeOffset To,
    string TimeZone,
    int Total,
    IReadOnlyList<DailyPlays> Daily,
    int[][] Grid)
{
    public static ListeningActivity Build(IEnumerable<DateTimeOffset> plays, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        var firstDay = LocalDay(from, zone);
        var daily = new int[LocalDay(to, zone).DayNumber - firstDay.DayNumber + 1];
        var grid = new int[7][];
        for (var day = 0; day < 7; day++)
            grid[day] = new int[24];

        var total = 0;
        foreach (var played in plays)
        {
            var local = TimeZoneInfo.ConvertTime(played, zone);
            grid[((int)local.DayOfWeek + 6) % 7][local.Hour]++;
            var index = DateOnly.FromDateTime(local.DateTime).DayNumber - firstDay.DayNumber;
            if (index >= 0 && index < daily.Length)
                daily[index]++;
            total++;
        }

        return new ListeningActivity(from, to, zone.Id, total, [.. daily.Select((n, i) => new DailyPlays(firstDay.AddDays(i), n))], grid);
    }

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}

/// <summary>Les artistes les plus écoutés entre deux dates, avec leur part du total.</summary>
public sealed record Soundtrack(DateOnly From, DateOnly To, int Total, IReadOnlyList<SoundtrackArtist> Artists)
{
    public static Soundtrack Build(DateOnly from, DateOnly to, IReadOnlyList<ArtistPlays> chart, int limit)
    {
        var total = chart.Sum(a => a.Plays);
        var artists = chart
            .OrderByDescending(a => a.Plays)
            .Take(limit)
            .Select(a => new SoundtrackArtist(a.Name, a.Plays, total == 0 ? 0 : Math.Round((double)a.Plays / total, 3), a.Url))
            .ToList();

        return new Soundtrack(from, to, total, artists);
    }
}

public sealed record SoundtrackArtist(string Name, int Plays, double Share, string Url);
