using System.Text.Json.Serialization;

namespace Portfolio.Api.Features.Music.Artwork;

internal sealed record DeezerSearch<T>(IReadOnlyList<T>? Data, DeezerError? Error);

internal sealed record DeezerError(string Message);

internal sealed record DeezerArtist(string Name, [property: JsonPropertyName("picture_xl")] string? PictureXl);

internal sealed record DeezerTrack(DeezerArtistRef Artist, DeezerAlbumRef Album);

internal sealed record DeezerAlbum(DeezerArtistRef Artist, [property: JsonPropertyName("cover_xl")] string? CoverXl);

internal sealed record DeezerArtistRef(string Name);

internal sealed record DeezerAlbumRef([property: JsonPropertyName("cover_xl")] string? CoverXl);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DeezerSearch<DeezerArtist>), TypeInfoPropertyName = "ArtistSearch")]
[JsonSerializable(typeof(DeezerSearch<DeezerTrack>), TypeInfoPropertyName = "TrackSearch")]
[JsonSerializable(typeof(DeezerSearch<DeezerAlbum>), TypeInfoPropertyName = "AlbumSearch")]
internal sealed partial class DeezerJson : JsonSerializerContext;
