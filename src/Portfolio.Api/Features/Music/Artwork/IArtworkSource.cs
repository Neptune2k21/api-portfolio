namespace Portfolio.Api.Features.Music.Artwork;

/// <summary>Visuels que Last.fm ne fournit plus (photos d'artistes) ou pas toujours (pochettes). Null si introuvable.</summary>
public interface IArtworkSource
{
    Task<string?> FindArtistImageAsync(string artist, CancellationToken ct);

    Task<string?> FindTrackCoverAsync(string artist, string title, CancellationToken ct);

    Task<string?> FindAlbumCoverAsync(string artist, string album, CancellationToken ct);
}
