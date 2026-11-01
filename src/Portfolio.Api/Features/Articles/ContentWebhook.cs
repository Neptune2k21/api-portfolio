using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Portfolio.Api.Features.Articles;

internal sealed record PushEvent(string Ref);

/// <summary>
/// Reçoit les push du dépôt de contenu : l'index est rechargé, puis le front régénère ses pages.
/// Hors de la clé d'API (GitHub ne la connaît pas) : chaque livraison est signée en HMAC SHA-256 avec le secret du webhook.
/// </summary>
public static class ContentWebhook
{
    public const string SignatureHeader = "X-Hub-Signature-256";
    public const string EventHeader = "X-GitHub-Event";

    public static IEndpointRouteBuilder MapContentWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/github", Receive);
        return app;
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound, BadRequest>> Receive(
        HttpRequest request, ArticleStore store, FrontRevalidator front, IOptions<ArticlesOptions> options)
    {
        if (options.Value.WebhookSecret is not { Length: > 0 } secret)
            return TypedResults.NotFound();

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        var body = buffer.ToArray();
        if (!IsSigned(body, request.Headers[SignatureHeader].ToString(), secret))
            return TypedResults.Unauthorized();

        // « ping » à la création du webhook, ou tout autre événement coché par erreur.
        if (request.Headers[EventHeader] != "push")
            return TypedResults.NoContent();

        PushEvent? push;
        try
        {
            push = JsonSerializer.Deserialize(body, ArticlesJson.Default.PushEvent);
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest();
        }

        if (push?.Ref != $"refs/heads/{options.Value.Branch}")
            return TypedResults.NoContent();

        // Pas le jeton de la requête : GitHub peut couper avant la fin, le rechargement doit aller au bout.
        await store.RefreshAsync(CancellationToken.None);
        await front.RevalidateAsync(CancellationToken.None);
        return TypedResults.NoContent();
    }

    internal static bool IsSigned(ReadOnlySpan<byte> body, string header, string secret)
    {
        const string prefix = "sha256=";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(header.AsSpan(prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body), provided);
    }
}
