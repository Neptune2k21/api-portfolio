using Microsoft.AspNetCore.Http.HttpResults;
using Portfolio.Api.Common;

namespace Portfolio.Api.Features.Articles;

public static class ArticleEndpoints
{
    public static RouteGroupBuilder MapArticles(this RouteGroupBuilder api)
    {
        var articles = api.MapGroup("/articles");
        articles.MapGet("", GetList);
        articles.MapGet("/{slug}", GetOne);
        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<ArticleSummary>>, ValidationProblem>> GetList(
        ArticleStore store, CancellationToken ct, string? kind = null, string? tag = null)
    {
        if (Validate.Query((kind is null || ArticleKind.IsValid(kind), nameof(kind), $"Valeurs possibles : {string.Join(", ", ArticleKind.All)}.")) is { } problem)
            return problem;

        IReadOnlyList<ArticleSummary> list = [.. (await store.GetVisibleAsync(ct))
            .Where(a => (kind is null || a.Kind == kind) && (tag is null || a.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Select(a => a.ToSummary())];
        return TypedResults.Ok(list);
    }

    private static async Task<Results<Ok<Article>, NotFound>> GetOne(string slug, ArticleStore store, CancellationToken ct) =>
        (await store.GetVisibleAsync(ct)).FirstOrDefault(a => a.Slug == slug) is { } article
            ? TypedResults.Ok(article)
            : TypedResults.NotFound();
}
