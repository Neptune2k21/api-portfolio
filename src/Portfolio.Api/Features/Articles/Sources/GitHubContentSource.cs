using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using Polly;
using Portfolio.Api.Common;

namespace Portfolio.Api.Features.Articles.Sources;

/// <summary>
/// Lit le dépôt de contenu par l'API GraphQL de GitHub : une requête pour l'arbre et le texte des fichiers,
/// puis une par lot de fichiers pour leurs historiques, au lieu d'un appel REST par fichier.
/// </summary>
internal sealed class GitHubContentSource(IHttpClientFactory clients, IOptions<ArticlesOptions> options, ILogger<GitHubContentSource> logger)
    : IContentSource
{
    public const string ClientName = "GitHub";
    public const string BaseUrl = "https://api.github.com/";

    private const string Service = "GitHub";
    private const int HistoryBatchSize = 50;
    private const int MaxRevisions = 100;

    // Deux niveaux suffisent : des dossiers à la racine, des fichiers dedans.
    private const string TreeQuery = """
        query($owner: String!, $name: String!, $tree: String!) {
          repository(owner: $owner, name: $name) {
            object(expression: $tree) {
              ... on Tree { entries { path type object {
                ... on Tree { entries { path type object { ... on Blob { text isTruncated } } } }
              } } }
            }
          }
        }
        """;

    private readonly ArticlesOptions _options = options.Value;

    public async Task<IReadOnlyList<ContentFile>> LoadAsync(CancellationToken ct)
    {
        var http = clients.CreateClient(ClientName);
        var tree = await QueryAsync(http, TreeQuery, Variables(("tree", $"{_options.Branch}:")), GitHubJson.Default.GraphQlResponseTreeData, ct);

        // Un dépôt encore vide n'a pas d'arbre.
        var markdown = (tree.Repository?.Object?.Entries ?? [])
            .Where(e => e.Type == "tree")
            .SelectMany(folder => folder.Object?.Entries ?? [])
            .Where(e => e.Type == "blob" && e.Path.EndsWith(".md", StringComparison.Ordinal))
            .ToList();

        foreach (var truncated in markdown.Where(e => e.Object?.IsTruncated == true))
            logger.LogWarning("{Path} ignoré : GitHub a tronqué son contenu", truncated.Path);

        var files = markdown.Where(e => e.Object is { Text: not null, IsTruncated: false }).ToList();
        var histories = new Dictionary<string, IReadOnlyList<Revision>>(StringComparer.Ordinal);
        foreach (var batch in files.Chunk(HistoryBatchSize))
        {
            var variables = Variables(("branch", $"refs/heads/{_options.Branch}"));
            for (var i = 0; i < batch.Length; i++)
                variables[$"p{i}"] = batch[i].Path;

            var data = await QueryAsync(http, HistoryQuery(batch.Length), variables, GitHubJson.Default.GraphQlResponseHistoryData, ct);
            var target = data.Repository?.Ref?.Target ?? throw new UpstreamException(Service, $"Branche {_options.Branch} introuvable.");
            for (var i = 0; i < batch.Length; i++)
            {
                histories[batch[i].Path] = target.TryGetValue($"f{i}", out var history)
                    ? [.. history.Nodes.Select(c => new Revision(c.Oid, c.AuthoredDate, c.MessageHeadline, c.Url))]
                    : [];
            }
        }

        return [.. files.Select(f => new ContentFile(f.Path, f.Object!.Text!, histories[f.Path]))];
    }

    // Les chemins passent en variables : rien du dépôt n'est recopié dans le texte de la requête.
    private static string HistoryQuery(int files)
    {
        var query = new StringBuilder("query($owner: String!, $name: String!, $branch: String!");
        for (var i = 0; i < files; i++)
            query.Append(CultureInfo.InvariantCulture, $", $p{i}: String!");
        query.Append(") { repository(owner: $owner, name: $name) { ref(qualifiedName: $branch) { target { ... on Commit {");
        for (var i = 0; i < files; i++)
            query.Append(CultureInfo.InvariantCulture, $" f{i}: history(path: $p{i}, first: {MaxRevisions}) {{ nodes {{ oid authoredDate messageHeadline url }} }}");
        return query.Append(" } } } } }").ToString();
    }

    private Dictionary<string, string> Variables(params ReadOnlySpan<(string Name, string Value)> extra)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal) { ["owner"] = _options.Owner, ["name"] = _options.Repository };
        foreach (var (name, value) in extra)
            variables[name] = value;
        return variables;
    }

    private static async Task<T> QueryAsync<T>(
        HttpClient http, string query, Dictionary<string, string> variables, JsonTypeInfo<GraphQlResponse<T>> type, CancellationToken ct)
        where T : class
    {
        try
        {
            using var response = await http.PostAsJsonAsync("graphql", new GraphQlRequest(query, variables), GitHubJson.Default.GraphQlRequest, ct);
            if (!response.IsSuccessStatusCode)
                throw new UpstreamException(Service, $"HTTP {(int)response.StatusCode}");

            var result = await response.Content.ReadFromJsonAsync(type, ct);
            if (result?.Errors is [var error, ..])
                throw new UpstreamException(Service, error.Message);
            return result?.Data ?? throw new UpstreamException(Service, "Réponse vide.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or ExecutionRejectedException)
        {
            throw new UpstreamException(Service, e.Message, e);
        }
    }
}
