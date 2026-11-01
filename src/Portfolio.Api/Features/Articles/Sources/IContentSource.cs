namespace Portfolio.Api.Features.Articles.Sources;

/// <summary>Un fichier markdown du dépôt de contenu, chemin relatif à sa racine, avec ses commits du plus récent au plus ancien.</summary>
public sealed record ContentFile(string Path, string Text, IReadOnlyList<Revision> History);

public interface IContentSource
{
    /// <summary>Les fichiers markdown de chaque dossier à la racine du dépôt.</summary>
    Task<IReadOnlyList<ContentFile>> LoadAsync(CancellationToken ct);
}
