namespace Portfolio.Api.Features.Articles;

/// <summary>Types de contenu, un par dossier à la racine du dépôt de contenu.</summary>
public static class ArticleKind
{
    public const string Article = "article";
    public const string Note = "note";
    public const string Journal = "journal";

    private static readonly Dictionary<string, string> ByFolder = new(StringComparer.Ordinal)
    {
        ["articles"] = Article,
        ["notes"] = Note,
        ["journal"] = Journal,
    };

    public static readonly IReadOnlyList<string> All = [Article, Note, Journal];

    public static string? FromFolder(string folder) => ByFolder.GetValueOrDefault(folder);

    public static bool IsValid(string kind) => All.Contains(kind);
}

/// <summary>Un commit qui a touché le fichier. <see cref="Url"/> est null pour un commit qui n'existe qu'en local.</summary>
public sealed record Revision(string Id, DateTimeOffset Date, string Message, string? Url);

public sealed record ArticleSummary(
    string Slug,
    string Kind,
    string Title,
    string? Summary,
    IReadOnlyList<string> Tags,
    DateOnly? PublishedAt,
    int ReadingMinutes,
    bool Draft);

/// <summary>
/// Un contenu complet. <see cref="StartedAt"/> (premier commit) et <see cref="PublishedAt"/> bornent la période d'écriture,
/// celle de sa bande-son. <see cref="History"/> va du commit le plus récent au plus ancien.
/// </summary>
public sealed record Article(
    string Slug,
    string Kind,
    string Title,
    string? Summary,
    IReadOnlyList<string> Tags,
    DateOnly? PublishedAt,
    bool Draft,
    int ReadingMinutes,
    string Body,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<Revision> History)
{
    public ArticleSummary ToSummary() => new(Slug, Kind, Title, Summary, Tags, PublishedAt, ReadingMinutes, Draft);

    /// <summary>Ni brouillon, ni programmé pour plus tard.</summary>
    public bool IsPublishedOn(DateOnly today) => !Draft && PublishedAt <= today;
}
