using System.Text.RegularExpressions;
using Portfolio.Api.Features.Articles.Sources;

namespace Portfolio.Api.Features.Articles;

internal static partial class ArticleParser
{
    private const int WordsPerMinute = 220;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    /// <summary>
    /// Tous les contenus valides, du plus récent au plus ancien, brouillons en tête.
    /// Un fichier invalide est écarté avec un avertissement : il ne fait jamais tomber le reste.
    /// </summary>
    public static IReadOnlyList<Article> ParseAll(IEnumerable<ContentFile> files, ILogger logger)
    {
        var articles = new List<Article>();
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                if (Parse(file) is not { } article)
                    continue;
                if (slugs.Add(article.Slug))
                    articles.Add(article);
                else
                    logger.LogWarning("{Path} ignoré : le slug {Slug} est déjà pris", file.Path, article.Slug);
            }
            catch (FormatException e)
            {
                logger.LogWarning("{Path} ignoré : {Reason}", file.Path, e.Message);
            }
        }

        return [.. articles.OrderByDescending(a => a.PublishedAt ?? DateOnly.MaxValue).ThenBy(a => a.Slug, StringComparer.Ordinal)];
    }

    /// <summary>Null pour un fichier qui n'est pas un contenu : hors des dossiers connus, ou pas en markdown.</summary>
    public static Article? Parse(ContentFile file)
    {
        if (file.Path.Split('/') is not [var folder, var name] || ArticleKind.FromFolder(folder) is not { } kind || !name.EndsWith(".md", StringComparison.Ordinal))
            return null;

        var slug = name[..^".md".Length];
        if (!SlugPattern().IsMatch(slug))
            throw new FormatException($"« {slug} » n'est pas un slug valide : minuscules, chiffres et tirets uniquement.");

        var (header, body) = FrontMatter.Parse(file.Text);
        var title = header.String("title") ?? throw new FormatException("« title » est obligatoire.");
        var summary = header.String("summary");
        var tags = header.List("tags");
        var publishedAt = header.Date("date");
        var draft = header.Bool("draft") ?? false;
        header.EnsureAllRead();

        if (!draft && publishedAt is null)
            throw new FormatException("« date » est obligatoire, sauf pour un brouillon (draft: true).");

        return new Article(slug, kind, title, summary, tags, publishedAt, draft, ReadingMinutes(body), body,
            StartedAt: file.History.Count > 0 ? file.History[^1].Date : null,
            UpdatedAt: file.History.Count > 0 ? file.History[0].Date : null,
            file.History);
    }

    /// <summary>Le code et les lignes de directive (<c>::terminal</c>…) se survolent : seule la prose est comptée.</summary>
    internal static int ReadingMinutes(string body)
    {
        var words = 0;
        var inCode = false;
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                inCode = !inCode;
            else if (!inCode && !trimmed.StartsWith("::", StringComparison.Ordinal))
                words += trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));
        }

        return Math.Max(1, (int)Math.Ceiling(words / (double)WordsPerMinute));
    }
}
