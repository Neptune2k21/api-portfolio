namespace Portfolio.Api.Features.Music.LastFm;

/// <summary>Lecture du compte Last.fm configuré. Les bizarreries de l'API restent derrière cette interface.</summary>
public interface ILastFmClient
{
    /// <summary>Les dernières écoutes, la plus récente d'abord ; inclut le morceau en cours s'il y en a un.</summary>
    Task<IReadOnlyList<Scrobble>> GetRecentAsync(int limit, CancellationToken ct);

    /// <summary>Les instants de chaque écoute terminée sur la période.</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetPlayTimesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<ListenerProfile> GetProfileAsync(CancellationToken ct);

    Task<IReadOnlyList<ArtistPlays>> GetTopArtistsAsync(string period, int limit, CancellationToken ct);

    Task<IReadOnlyList<TrackPlays>> GetTopTracksAsync(string period, int limit, CancellationToken ct);

    Task<IReadOnlyList<AlbumPlays>> GetTopAlbumsAsync(string period, int limit, CancellationToken ct);

    /// <summary>Tous les artistes écoutés sur une période quelconque.</summary>
    Task<IReadOnlyList<ArtistPlays>> GetArtistChartAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
