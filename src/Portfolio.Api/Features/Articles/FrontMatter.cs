using System.Globalization;

namespace Portfolio.Api.Features.Articles;

/// <summary>
/// En-tête d'un fichier markdown, sous-ensemble de YAML suffisant pour le contenu :
/// une clé par ligne, valeur simple (guillemets facultatifs) ou liste en ligne <c>[a, b]</c>.
/// Chaque clé lue est consommée ; <see cref="EnsureAllRead"/> signale celles qui restent, souvent une faute de frappe.
/// </summary>
internal sealed class FrontMatter
{
    private const string Fence = "---";

    private readonly Dictionary<string, string> _values;

    private FrontMatter(Dictionary<string, string> values) => _values = values;

    public static (FrontMatter Header, string Body) Parse(string text)
    {
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        if (reader.ReadLine()?.Trim() != Fence)
            throw new FormatException("Le fichier doit commencer par un en-tête « --- ».");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Trim() == Fence)
                return (new FrontMatter(values), reader.ReadToEnd().TrimStart('\r', '\n'));
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                throw new FormatException($"Ligne {lineNumber} de l'en-tête : « clé: valeur » attendu.");

            var key = line[..colon].Trim();
            if (!values.TryAdd(key, line[(colon + 1)..].Trim()))
                throw new FormatException($"Ligne {lineNumber} de l'en-tête : « {key} » est défini deux fois.");
        }

        throw new FormatException("L'en-tête n'est jamais refermé par « --- ».");
    }

    public string? String(string key) => Take(key) is { } raw && Unquote(raw) is { Length: > 0 } value ? value : null;

    public IReadOnlyList<string> List(string key)
    {
        if (Take(key) is not { } raw)
            return [];

        var items = raw.StartsWith('[') && raw.EndsWith(']') ? raw[1..^1].Split(',') : [raw];
        return [.. items.Select(i => Unquote(i.Trim())).Where(i => i.Length > 0)];
    }

    public bool? Bool(string key) => Take(key) switch
    {
        null => null,
        "true" => true,
        "false" => false,
        var raw => throw new FormatException($"« {key} » vaut true ou false, pas « {raw} »."),
    };

    public DateOnly? Date(string key)
    {
        if (Take(key) is not { } raw)
            return null;

        return DateOnly.TryParseExact(Unquote(raw), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new FormatException($"« {key} » attend une date AAAA-MM-JJ, pas « {raw} ».");
    }

    public void EnsureAllRead()
    {
        if (_values.Count > 0)
            throw new FormatException($"Clé inconnue dans l'en-tête : {string.Join(", ", _values.Keys)}.");
    }

    private string? Take(string key) => _values.Remove(key, out var raw) && raw.Length > 0 ? raw : null;

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value;
}
