using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Portfolio.Api.Features.Articles.Sources;

namespace Portfolio.Api.Features.Articles;

public sealed class ArticlesOptions
{
    public const string Section = "Articles";

    /// <summary>Copie locale du dépôt de contenu. Renseignée, elle remplace GitHub : on écrit et on prévisualise sans pousser.</summary>
    public string? LocalPath { get; set; }

    public string Owner { get; set; } = "Neptune2k21";

    public string Repository { get; set; } = "contenu-portfolio";

    public string Branch { get; set; } = "master";

    /// <summary>Jeton en lecture seule sur le dépôt de contenu ; l'API GraphQL l'exige même pour un dépôt public.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>Secret du webhook GitHub. Sans lui, le webhook répond 404.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Route du front qui régénère les pages d'articles, appelée après chaque push.</summary>
    public string? RevalidateUrl { get; set; }

    /// <summary>Sert aussi les brouillons et les articles programmés, pour les relire en local.</summary>
    public bool IncludeDrafts { get; set; }

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Fuseau dans lequel une date de publication commence.</summary>
    public string TimeZone { get; set; } = "Europe/Paris";
}

public static class ArticlesModule
{
    public static IServiceCollection AddArticles(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ArticlesOptions>()
            .Bind(config.GetSection(ArticlesOptions.Section))
            .Validate(o => o.LocalPath is { Length: > 0 } || o.GitHubToken is { Length: > 0 },
                "Articles:GitHubToken est obligatoire (ou Articles:LocalPath pour lire une copie locale).")
            .Validate(o => o.LocalPath is not { Length: > 0 } || Directory.Exists(o.LocalPath), "Articles:LocalPath n'existe pas.")
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Articles:TimeZone est inconnu (tzdata absent ?).")
            .ValidateOnStart();

        if (string.IsNullOrWhiteSpace(config[$"{ArticlesOptions.Section}:{nameof(ArticlesOptions.LocalPath)}"]))
        {
            services.AddHttpClient(GitHubContentSource.ClientName, (sp, http) =>
                {
                    http.BaseAddress = new Uri(GitHubContentSource.BaseUrl);
                    http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("api-portfolio", null));
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", sp.GetRequiredService<IOptions<ArticlesOptions>>().Value.GitHubToken);
                })
                .AddStandardResilienceHandler(o =>
                {
                    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
                });
            services.AddSingleton<IContentSource, GitHubContentSource>();
        }
        else
        {
            services.AddSingleton<IContentSource, LocalContentSource>();
        }

        services.AddHttpClient<FrontRevalidator>()
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        services.AddSingleton<ArticleStore>();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Add(ArticlesJson.Default));
        return services;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Article))]
[JsonSerializable(typeof(IReadOnlyList<ArticleSummary>))]
[JsonSerializable(typeof(PushEvent))]
internal sealed partial class ArticlesJson : JsonSerializerContext;
