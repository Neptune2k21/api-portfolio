using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Api.Common;
using Portfolio.Api.Features.Articles;
using Portfolio.Api.Features.Articles.Sources;

namespace Portfolio.Api.Tests.Articles;

public sealed class ArticlesApiTests
{
    private const string WebhookSecret = "webhook-secret";

    // Le 3 novembre 2026 à 0 h 30 à Paris, encore le 2 en UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_hides_drafts_and_articles_scheduled_for_later()
    {
        await using var api = new ApiFactory();
        api.Content.Files =
        [
            File("articles/aujourdhui.md", "date: 2026-11-03"),
            File("articles/demain.md", "date: 2026-11-04"),
            File("articles/brouillon.md", "draft: true"),
        ];

        var articles = await api.Client().GetFromJsonAsync<ArticleSummary[]>("/api/articles");

        Assert.Equal(["aujourdhui"], articles!.Select(a => a.Slug));
    }

    [Fact]
    public async Task List_filters_by_kind_and_tag()
    {
        await using var api = new ApiFactory();
        api.Content.Files =
        [
            File("articles/aot.md", "date: 2026-10-01\ntags: [dotnet, cloud]"),
            File("notes/spotify.md", "date: 2026-10-02\ntags: [Music]"),
            File("notes/lanyard.md", "date: 2026-10-03\ntags: [dotnet]"),
        ];

        var notes = await api.Client().GetFromJsonAsync<ArticleSummary[]>("/api/articles?kind=note&tag=dotnet");

        Assert.Equal(["lanyard"], notes!.Select(a => a.Slug));
    }

    [Fact]
    public async Task List_rejects_an_unknown_kind()
    {
        await using var api = new ApiFactory();

        var response = await api.Client().GetAsync("/api/articles?kind=video");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Article_returns_its_body_and_history()
    {
        await using var api = new ApiFactory();
        api.Content.Files = [File("articles/aot.md", "date: 2026-10-01", "Corps de l'article", new Revision("c1", Now, "Premier jet", "https://github.com/c1"))];

        var article = await api.Client().GetFromJsonAsync<Article>("/api/articles/aot");

        Assert.Equal("Corps de l'article", article!.Body);
        Assert.Equal(Now, article.StartedAt);
        Assert.Equal("Premier jet", Assert.Single(article.History).Message);
    }

    [Fact]
    public async Task A_draft_cannot_be_read_by_its_slug()
    {
        await using var api = new ApiFactory();
        api.Content.Files = [File("articles/brouillon.md", "draft: true")];

        var response = await api.Client().GetAsync("/api/articles/brouillon");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Drafts_are_served_when_enabled_for_proofreading()
    {
        await using var api = new ApiFactory(includeDrafts: true);
        api.Content.Files = [File("articles/brouillon.md", "draft: true")];

        var articles = await api.Client().GetFromJsonAsync<ArticleSummary[]>("/api/articles");

        Assert.True(Assert.Single(articles!).Draft);
    }

    [Fact]
    public async Task The_previous_index_is_served_while_the_source_is_down()
    {
        await using var api = new ApiFactory();
        api.Content.Files = [File("articles/aot.md", "date: 2026-10-01")];
        var client = api.Client();
        await client.GetAsync("/api/articles");

        api.Content.Failure = new UpstreamException("GitHub", "down");
        api.Time.Now += TimeSpan.FromDays(1);
        var articles = await client.GetFromJsonAsync<ArticleSummary[]>("/api/articles");

        Assert.Equal(["aot"], articles!.Select(a => a.Slug));
    }

    [Fact]
    public async Task Source_down_on_the_first_load_is_a_bad_gateway()
    {
        await using var api = new ApiFactory();
        api.Content.Failure = new UpstreamException("GitHub", "down");

        var response = await api.Client().GetAsync("/api/articles");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_rejects_an_unsigned_delivery()
    {
        await using var api = new ApiFactory();

        var response = await api.CreateClient().SendAsync(Delivery("push", """{"ref":"refs/heads/master"}""", signature: "sha256=00"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_push_reloads_the_index_without_waiting_for_the_refresh_interval()
    {
        await using var api = new ApiFactory();
        var client = api.Client();
        Assert.Empty((await client.GetFromJsonAsync<ArticleSummary[]>("/api/articles"))!);

        api.Content.Files = [File("articles/nouveau.md", "date: 2026-11-01")];
        var response = await api.CreateClient().SendAsync(Delivery("push", """{"ref":"refs/heads/master"}"""));
        var articles = await client.GetFromJsonAsync<ArticleSummary[]>("/api/articles");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["nouveau"], articles!.Select(a => a.Slug));
    }

    [Theory]
    [InlineData("ping", """{"zen":"Keep it logically awesome."}""")]
    [InlineData("push", """{"ref":"refs/heads/autre-branche"}""")]
    public async Task Webhook_ignores_events_that_do_not_change_the_published_branch(string eventName, string payload)
    {
        await using var api = new ApiFactory();
        await api.Client().GetAsync("/api/articles");

        var response = await api.CreateClient().SendAsync(Delivery(eventName, payload));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, api.Content.Loads);
    }

    private static ContentFile File(string path, string header, string body = "", params Revision[] history) =>
        new(path, $"---\ntitle: {path}\n{header}\n---\n{body}", history);

    private static HttpRequestMessage Delivery(string eventName, string payload, string? signature = null)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github") { Content = new ByteArrayContent(body) };
        request.Headers.Add(ContentWebhook.EventHeader, eventName);
        request.Headers.Add(ContentWebhook.SignatureHeader,
            signature ?? $"sha256={Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), body))}");
        return request;
    }

    private sealed class ApiFactory(bool includeDrafts = false) : WebApplicationFactory<Program>
    {
        public FakeContent Content { get; } = new();
        public ManualTime Time { get; } = new(Now);

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
            builder.UseSetting("Articles:WebhookSecret", WebhookSecret);
            builder.UseSetting("Articles:IncludeDrafts", includeDrafts ? "true" : "false");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IContentSource>(Content);
                services.AddSingleton<TimeProvider>(Time);
            });
        }
    }

    private sealed class FakeContent : IContentSource
    {
        public IReadOnlyList<ContentFile> Files { get; set; } = [];
        public Exception? Failure { get; set; }
        public int Loads { get; private set; }

        public Task<IReadOnlyList<ContentFile>> LoadAsync(CancellationToken ct)
        {
            Loads++;
            return Failure is null ? Task.FromResult(Files) : Task.FromException<IReadOnlyList<ContentFile>>(Failure);
        }
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
