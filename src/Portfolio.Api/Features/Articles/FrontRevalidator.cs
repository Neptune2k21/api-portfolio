using Microsoft.Extensions.Options;
using Polly;
using Portfolio.Api.Common;

namespace Portfolio.Api.Features.Articles;

/// <summary>Demande au front de jeter ses pages d'articles en cache. Sans URL configurée (en local), ne fait rien.</summary>
public sealed class FrontRevalidator(HttpClient http, IOptions<ArticlesOptions> articles, IOptions<ApiKeyOptions> apiKey)
{
    private const string Service = "Front";

    public async Task RevalidateAsync(CancellationToken ct)
    {
        if (articles.Value.RevalidateUrl is not { Length: > 0 } url)
            return;

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        // La clé partagée sert aussi dans ce sens : le front la connaît déjà.
        if (apiKey.Value.Key is { Length: > 0 } key)
            request.Headers.Add(ApiKeyFilter.Header, key);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new UpstreamException(Service, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or ExecutionRejectedException)
        {
            throw new UpstreamException(Service, e.Message, e);
        }
    }
}
