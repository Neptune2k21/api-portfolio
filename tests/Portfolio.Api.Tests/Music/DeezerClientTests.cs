using System.Net;
using System.Text;
using Portfolio.Api.Common;
using Portfolio.Api.Features.Music.Artwork;

namespace Portfolio.Api.Tests.Music;

public sealed class DeezerClientTests
{
    [Theory]
    [InlineData("bello&dallas", "Bello&Dallas")]
    [InlineData("WeRenoi", "Werenoi")]
    [InlineData("Mous-K", "Mous K")]
    public void Artist_names_match_regardless_of_case_and_punctuation(string lastFm, string deezer) =>
        Assert.True(DeezerClient.SameArtist(lastFm, deezer));

    [Fact]
    public async Task Picks_the_first_result_from_the_right_artist()
    {
        var client = Client("""
            {"data":[
              {"name":"Lagui Tribute","picture_xl":"https://cdn/artist/wrong/1000x1000.jpg"},
              {"name":"Lagui","picture_xl":"https://cdn/artist/right/1000x1000.jpg"}
            ]}
            """);

        Assert.Equal("https://cdn/artist/right/1000x1000.jpg", await client.FindArtistImageAsync("lagui", CancellationToken.None));
    }

    [Fact]
    public async Task Placeholder_picture_counts_as_missing()
    {
        var client = Client("""{"data":[{"name":"Lagui","picture_xl":"https://cdn/images/artist//1000x1000-000000-80-0-0.jpg"}]}""");

        Assert.Null(await client.FindArtistImageAsync("Lagui", CancellationToken.None));
    }

    [Fact]
    public async Task Error_inside_a_success_response_becomes_an_upstream_exception()
    {
        var client = Client("""{"error":{"type":"Exception","message":"Quota limit exceeded","code":4}}""");

        await Assert.ThrowsAsync<UpstreamException>(() => client.FindArtistImageAsync("Lagui", CancellationToken.None));
    }

    private static DeezerClient Client(string json) =>
        new(new HttpClient(new StubHandler(json)) { BaseAddress = new Uri(DeezerClient.BaseUrl) });

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
