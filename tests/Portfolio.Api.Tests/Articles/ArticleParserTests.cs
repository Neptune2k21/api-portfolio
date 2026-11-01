using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Api.Features.Articles;
using Portfolio.Api.Features.Articles.Sources;

namespace Portfolio.Api.Tests.Articles;

public sealed class ArticleParserTests
{
    [Fact]
    public void Header_reads_quoted_values_inline_lists_and_leaves_the_body_intact()
    {
        var article = Parse("articles/native-aot.md", """
            ---
            title: "Native AOT : 69 Mo"
            summary: 'Une API .NET sur Cloud Run'
            date: 2026-11-03
            tags: [dotnet, "cloud run"]
            ---

            Le premier démarrage à froid…
            """);

        Assert.Equal("native-aot", article.Slug);
        Assert.Equal(ArticleKind.Article, article.Kind);
        Assert.Equal("Native AOT : 69 Mo", article.Title);
        Assert.Equal("Une API .NET sur Cloud Run", article.Summary);
        Assert.Equal(new DateOnly(2026, 11, 3), article.PublishedAt);
        Assert.Equal(["dotnet", "cloud run"], article.Tags);
        Assert.Equal("Le premier démarrage à froid…", article.Body);
    }

    [Theory]
    [InlineData("title: Sans en-tête", "commencer")]
    [InlineData("---\ntitle: Jamais refermé", "refermé")]
    [InlineData("---\ntitle: A\ntitle: B\n---", "deux fois")]
    [InlineData("---\ntitle: A\ndate: 2026-11-03\ntitel: faute\n---", "titel")]
    [InlineData("---\ntitle: A\ndate: 03/11/2026\n---", "AAAA-MM-JJ")]
    [InlineData("---\ntitle: A\n---", "date")]
    [InlineData("---\ndate: 2026-11-03\n---", "title")]
    public void Invalid_headers_explain_what_is_wrong(string text, string hint)
    {
        var error = Assert.Throws<FormatException>(() => Parse("notes/test.md", text));

        Assert.Contains(hint, error.Message);
    }

    [Fact]
    public void A_draft_needs_no_date()
    {
        var article = Parse("journal/2026-10.md", "---\ntitle: Octobre\ndraft: true\n---\n");

        Assert.True(article.Draft);
        Assert.Null(article.PublishedAt);
        Assert.Equal(ArticleKind.Journal, article.Kind);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("brouillons/idee.md")]
    [InlineData("articles/schema.png")]
    [InlineData("articles/images/schema.md")]
    public void Files_outside_content_folders_are_not_content(string path)
    {
        Assert.Null(ArticleParser.Parse(new ContentFile(path, "", [])));
    }

    [Fact]
    public void Slug_must_be_url_friendly()
    {
        Assert.Throws<FormatException>(() => Parse("articles/Mon Article.md", "---\ntitle: A\ndate: 2026-11-03\n---"));
    }

    [Fact]
    public void Writing_period_comes_from_the_oldest_and_newest_commits()
    {
        Revision[] history =
        [
            new("c3", new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), "Coquille", null),
            new("c1", new DateTimeOffset(2026, 10, 28, 21, 0, 0, TimeSpan.Zero), "Premier jet", null),
        ];

        var article = ArticleParser.Parse(new ContentFile("articles/a.md", "---\ntitle: A\ndate: 2026-11-03\n---", history))!;

        Assert.Equal(history[1].Date, article.StartedAt);
        Assert.Equal(history[0].Date, article.UpdatedAt);
    }

    [Fact]
    public void Reading_time_counts_prose_but_not_code_or_directives()
    {
        var prose = string.Join(' ', Enumerable.Repeat("mot", 300));
        var code = string.Join(' ', Enumerable.Repeat("token", 5000));
        var body = $"{prose}\n\n```csharp\n{code}\n```\n\n::terminal{{cmd=\"{code}\"}}\n";

        Assert.Equal(2, ArticleParser.ReadingMinutes(body));
    }

    [Fact]
    public void One_invalid_file_or_duplicate_slug_never_hides_the_others()
    {
        ContentFile[] files =
        [
            new("articles/ancien.md", "---\ntitle: Ancien\ndate: 2026-01-01\n---", []),
            new("articles/casse.md", "pas d'en-tête", []),
            new("notes/recent.md", "---\ntitle: Récent\ndate: 2026-11-01\n---", []),
            new("journal/recent.md", "---\ntitle: Doublon\ndate: 2026-11-02\n---", []),
            new("articles/wip.md", "---\ntitle: En cours\ndraft: true\n---", []),
        ];

        var articles = ArticleParser.ParseAll(files, NullLogger.Instance);

        Assert.Equal(["wip", "recent", "ancien"], articles.Select(a => a.Slug));
    }

    private static Article Parse(string path, string text) => ArticleParser.Parse(new ContentFile(path, text, []))!;
}
