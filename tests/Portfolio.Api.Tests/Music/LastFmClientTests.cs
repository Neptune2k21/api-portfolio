using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Portfolio.Api.Common;
using Portfolio.Api.Features.Music.LastFm;

namespace Portfolio.Api.Tests.Music;

public sealed class LastFmClientTests
{
    private const string NowPlaying = """
        {"artist":{"#text":"DA Uzi"},"album":{"#text":"ORIGINAL GANGSTA"},"name":"UFC 93","url":"https://last.fm/t/1",
         "image":[{"size":"small","#text":"https://img/34s/a.jpg"},{"size":"extralarge","#text":"https://img/300x300/a.jpg"}],
         "@attr":{"nowplaying":"true"}}
        """;

    private static string Played(long uts, string image = "https://img/300x300/b.jpg") => $$"""
        {"artist":{"#text":"Mous-K"},"album":{"#text":"Cercle"},"name":"Mous-K","url":"https://last.fm/t/2",
         "image":[{"size":"extralarge","#text":"{{image}}"}],"date":{"uts":"{{uts}}"} }
        """;

    private static string RecentPage(string tracks, int totalPages) =>
        $$"""{"recenttracks":{"track":{{tracks}},"@attr":{"totalPages":"{{totalPages}}"} } }""";

    [Fact]
    public async Task Single_track_sent_as_an_object_is_read_as_a_list()
    {
        var client = Client(_ => Json(RecentPage(NowPlaying, totalPages: 0)));

        var recent = await client.GetRecentAsync(1, CancellationToken.None);

        var track = Assert.Single(recent);
        Assert.True(track.NowPlaying);
        Assert.Equal("DA Uzi", track.Artist);
        Assert.Equal("https://img/300x300/a.jpg", track.CoverUrl);
    }

    [Fact]
    public async Task Placeholder_cover_is_dropped()
    {
        var placeholder = "https://img/300x300/2a96cbd8b46e442fc41c2b86b821562f.png";
        var client = Client(_ => Json(RecentPage($"[{Played(1_793_400_000, placeholder)}]", totalPages: 1)));

        var track = Assert.Single(await client.GetRecentAsync(1, CancellationToken.None));

        Assert.Null(track.CoverUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_793_400_000), track.PlayedAt);
    }

    [Fact]
    public async Task Play_times_cover_every_page_and_skip_the_track_playing_now()
    {
        var requestedPages = new List<string>();
        var client = Client(request =>
        {
            var page = QueryHelpers.ParseQuery(request.RequestUri!.Query)["page"].ToString();
            lock (requestedPages)
                requestedPages.Add(page);
            return Json(page == "1"
                ? RecentPage($"[{NowPlaying},{Played(100)}]", totalPages: 2)
                : RecentPage($"[{Played(50)}]", totalPages: 2));
        });

        var times = await client.GetPlayTimesAsync(DateTimeOffset.FromUnixTimeSeconds(0), DateTimeOffset.FromUnixTimeSeconds(200), CancellationToken.None);

        Assert.Equal([100L, 50L], times.Select(t => t.ToUnixTimeSeconds()));
        Assert.Equal(["1", "2"], requestedPages.Order());
    }

    [Fact]
    public async Task Error_response_becomes_an_upstream_exception()
    {
        var client = Client(_ => Json("""{"error":10,"message":"Invalid API key"}""", HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<UpstreamException>(() => client.GetProfileAsync(CancellationToken.None));

        Assert.Contains("Invalid API key", error.Message);
    }

    [Fact]
    public async Task Unexpected_payload_becomes_an_upstream_exception()
    {
        var client = Client(_ => Json("""{"error":6,"message":"User not found"}"""));

        await Assert.ThrowsAsync<UpstreamException>(() => client.GetProfileAsync(CancellationToken.None));
    }

    private static LastFmClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri(LastFmClient.BaseUrl) },
            Options.Create(new LastFmOptions { ApiKey = "key", User = "user" }));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
