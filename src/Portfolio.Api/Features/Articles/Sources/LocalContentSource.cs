using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Portfolio.Api.Features.Articles.Sources;

/// <summary>
/// Lit une copie locale du dépôt de contenu, pour écrire et prévisualiser sans pousser.
/// L'historique vient de <c>git log</c> : un fichier jamais commité n'en a pas encore.
/// </summary>
internal sealed class LocalContentSource(IOptions<ArticlesOptions> options, ILogger<LocalContentSource> logger) : IContentSource
{
    private const char Separator = '\x1f';

    public async Task<IReadOnlyList<ContentFile>> LoadAsync(CancellationToken ct)
    {
        var root = Path.GetFullPath(options.Value.LocalPath!);
        var paths = Directory.EnumerateDirectories(root)
            .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.md"))
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));

        return await Task.WhenAll(paths.Select(async path => new ContentFile(
            path,
            await File.ReadAllTextAsync(Path.Combine(root, path), ct),
            await GetHistoryAsync(root, path, ct))));
    }

    private async Task<IReadOnlyList<Revision>> GetHistoryAsync(string root, string path, CancellationToken ct)
    {
        var git = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in (string[])["log", $"--format=%H{Separator}%aI{Separator}%s", "--", path])
            git.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(git)!;
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var errors = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                logger.LogDebug("git log a échoué pour {Path} : {Error}", path, await errors);
                return [];
            }

            return [.. (await output)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(Separator))
                .Where(parts => parts.Length == 3)
                .Select(parts => new Revision(parts[0], DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], Url: null))];
        }
        catch (Win32Exception e)
        {
            logger.LogDebug(e, "git est introuvable, {Path} n'aura pas d'historique", path);
            return [];
        }
    }
}
