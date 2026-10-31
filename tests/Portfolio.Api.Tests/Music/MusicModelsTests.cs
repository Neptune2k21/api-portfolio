using Portfolio.Api.Features.Music;

namespace Portfolio.Api.Tests.Music;

public sealed class MusicModelsTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    [Fact]
    public void Activity_buckets_plays_by_local_day_and_hour_across_daylight_saving()
    {
        DateTimeOffset[] plays =
        [
            new(2026, 7, 1, 22, 30, 0, TimeSpan.Zero), // mercredi 22:30 UTC = jeudi 00:30 à Paris (UTC+2)
            new(2026, 1, 5, 22, 30, 0, TimeSpan.Zero), // lundi 22:30 UTC = lundi 23:30 à Paris (UTC+1)
        ];

        var activity = ListeningActivity.Build(plays, plays[1], plays[0], Paris);

        Assert.Equal(2, activity.Total);
        Assert.Equal(1, activity.Grid[3][0]);
        Assert.Equal(1, activity.Grid[0][23]);
        Assert.Equal(2, activity.Grid.Sum(day => day.Sum()));
    }

    [Fact]
    public void Activity_lists_every_local_day_including_empty_ones()
    {
        var from = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] plays = [new(2026, 10, 29, 23, 30, 0, TimeSpan.Zero)]; // 30 octobre, 00:30 à Paris

        var activity = ListeningActivity.Build(plays, from, to, Paris);

        Assert.Equal([new DateOnly(2026, 10, 27), new(2026, 10, 28), new(2026, 10, 29), new(2026, 10, 30)], activity.Daily.Select(d => d.Date));
        Assert.Equal([0, 0, 0, 1], activity.Daily.Select(d => d.Plays));
    }

    [Fact]
    public void Soundtrack_keeps_top_artists_with_their_share_of_all_plays()
    {
        ArtistPlays[] chart = [new("B", 10, "b"), new("A", 30, "a"), new("C", 10, "c")];

        var soundtrack = Soundtrack.Build(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), chart, limit: 2);

        Assert.Equal(50, soundtrack.Total);
        Assert.Equal(["A", "B"], soundtrack.Artists.Select(a => a.Name));
        Assert.Equal(0.6, soundtrack.Artists[0].Share);
    }

    [Fact]
    public void Soundtrack_without_plays_is_empty()
    {
        var soundtrack = Soundtrack.Build(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), [], limit: 5);

        Assert.Equal(0, soundtrack.Total);
        Assert.Empty(soundtrack.Artists);
    }
}
