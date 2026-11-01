using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Api.Common;
using Portfolio.Api.Features.Music;
using Portfolio.Api.Features.Music.Artwork;
using Portfolio.Api.Features.Music.LastFm;

namespace Portfolio.Api.Tests.Music;

public sealed class MusicApiTests
{
    [Fact]
    public async Task Requests_without_the_api_key_are_rejected()
    {
        await using var api = new ApiFactory();

        var response = await api.CreateClient().GetAsync("/api/music/latest");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_check_is_public()
    {
        await using var api = new ApiFactory();

        var response = await api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Latest_is_empty_when_nothing_was_ever_played()
    {
        await using var api = new ApiFactory();

        var response = await api.Client().GetAsync("/api/music/latest");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Latest_returns_the_track_playing_now()
    {
        await using var api = new ApiFactory();
        api.LastFm.Recent = [new Scrobble("UFC 93", "DA Uzi", "ORIGINAL GANGSTA", null, "https://last.fm/t/1", PlayedAt: null)];

        var track = await api.Client().GetFromJsonAsync<Scrobble>("/api/music/latest");

        Assert.NotNull(track);
        Assert.True(track.NowPlaying);
        Assert.Equal("UFC 93", track.Title);
    }

    [Fact]
    public async Task Top_charts_report_every_invalid_parameter()
    {
        await using var api = new ApiFactory();

        var response = await api.Client().GetAsync("/api/music/top/albums?period=year&limit=99");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["limit", "period"], problem!.Errors.Keys.Order());
    }

    [Fact]
    public async Task Missing_artwork_is_filled_and_an_unavailable_source_is_not_an_error()
    {
        await using var api = new ApiFactory();
        api.LastFm.Chart = [new ArtistPlays("Lagui", 3, "https://last.fm/a/lagui"), new ArtistPlays("Inconnu", 1, "https://last.fm/a/x")];
        api.Artwork.Images["Lagui"] = "https://img/lagui.jpg";

        var artists = await api.Client().GetFromJsonAsync<ArtistPlays[]>("/api/music/top/artists");

        Assert.Equal(["https://img/lagui.jpg", null], artists!.Select(a => a.ImageUrl));
    }

    [Theory]
    [InlineData("from=2026-10-31&to=2026-10-01")]
    [InlineData("from=2020-01-01&to=2026-01-01")]
    [InlineData("from=nope&to=2026-10-01")]
    [InlineData("to=2026-10-01")]
    public async Task Soundtrack_rejects_invalid_ranges(string query)
    {
        await using var api = new ApiFactory();

        var response = await api.Client().GetAsync($"/api/music/soundtrack?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Soundtrack_reads_the_chart_from_local_midnight_to_local_midnight()
    {
        await using var api = new ApiFactory();
        api.LastFm.Chart = [new ArtistPlays("bello&dallas", 34, "https://last.fm/a/1")];

        var soundtrack = await api.Client().GetFromJsonAsync<Soundtrack>("/api/music/soundtrack?from=2026-10-30&to=2026-10-31");

        Assert.Equal(1.0, Assert.Single(soundtrack!.Artists).Share);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.FromHours(1)), api.LastFm.ChartRange?.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(1)), api.LastFm.ChartRange?.To);
    }

    [Fact]
    public async Task Upstream_failure_is_a_bad_gateway()
    {
        await using var api = new ApiFactory();
        api.LastFm.Failure = new UpstreamException("Last.fm", "down");

        var response = await api.Client().GetAsync("/api/music/profile");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public FakeLastFm LastFm { get; } = new();
        public FakeArtwork Artwork { get; } = new();

        public HttpClient Client()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(ApiKeyFilter.Header, "test-key");
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Api:Key", "test-key");
            builder.UseSetting("LastFm:ApiKey", "unused");
            builder.UseSetting("LastFm:User", "tester");
            builder.UseSetting("Articles:GitHubToken", "unused");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILastFmClient>(LastFm);
                services.AddSingleton<IArtworkSource>(Artwork);
            });
        }
    }

    private sealed class FakeLastFm : ILastFmClient
    {
        public IReadOnlyList<Scrobble> Recent { get; set; } = [];
        public IReadOnlyList<ArtistPlays> Chart { get; set; } = [];
        public Exception? Failure { get; set; }
        public (DateTimeOffset From, DateTimeOffset To)? ChartRange { get; private set; }

        public Task<IReadOnlyList<Scrobble>> GetRecentAsync(int limit, CancellationToken ct) => Answer(Recent);

        public Task<IReadOnlyList<DateTimeOffset>> GetPlayTimesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Answer<IReadOnlyList<DateTimeOffset>>([]);

        public Task<ListenerProfile> GetProfileAsync(CancellationToken ct) =>
            Answer(new ListenerProfile(0, 0, 0, 0, DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<ArtistPlays>> GetTopArtistsAsync(string period, int limit, CancellationToken ct) => Answer(Chart);

        public Task<IReadOnlyList<TrackPlays>> GetTopTracksAsync(string period, int limit, CancellationToken ct) =>
            Answer<IReadOnlyList<TrackPlays>>([]);

        public Task<IReadOnlyList<AlbumPlays>> GetTopAlbumsAsync(string period, int limit, CancellationToken ct) =>
            Answer<IReadOnlyList<AlbumPlays>>([]);

        public Task<IReadOnlyList<ArtistPlays>> GetArtistChartAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        {
            ChartRange = (from, to);
            return Answer(Chart);
        }

        private Task<T> Answer<T>(T value) => Failure is null ? Task.FromResult(value) : Task.FromException<T>(Failure);
    }

    // Ne connaît que les artistes de Images ; pour les autres, la source est « en panne ».
    private sealed class FakeArtwork : IArtworkSource
    {
        public Dictionary<string, string> Images { get; } = [];

        public Task<string?> FindArtistImageAsync(string artist, CancellationToken ct) =>
            Images.TryGetValue(artist, out var url) ? Task.FromResult<string?>(url) : Task.FromException<string?>(new UpstreamException("Deezer", "down"));

        public Task<string?> FindTrackCoverAsync(string artist, string title, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<string?> FindAlbumCoverAsync(string artist, string album, CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
