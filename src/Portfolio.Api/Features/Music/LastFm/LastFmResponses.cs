using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Portfolio.Api.Features.Music.LastFm;

// Formes brutes des réponses Last.fm : nombres en chaînes, clés « #text » et « @attr »,
// listes renvoyées comme un simple objet quand elles ne contiennent qu'un élément.

internal sealed record RecentTracksResponse([property: JsonRequired] RecentTracksPage RecentTracks);

internal sealed record RecentTracksPage(
    [property: JsonConverter(typeof(OneOrManyConverter<RecentTrack>))] IReadOnlyList<RecentTrack>? Track,
    [property: JsonPropertyName("@attr")] Paging Attr);

internal sealed record Paging(int TotalPages);

internal sealed record RecentTrack(string Name, TextValue Artist, TextValue Album, IReadOnlyList<Image>? Image, string Url, PlayDate? Date);

internal sealed record TextValue([property: JsonPropertyName("#text")] string Text);

internal sealed record Image(string Size, [property: JsonPropertyName("#text")] string Url);

internal sealed record PlayDate(long Uts);

internal sealed record TopArtistsResponse([property: JsonRequired] ArtistList TopArtists);

internal sealed record ArtistChartResponse([property: JsonRequired] ArtistList WeeklyArtistChart);

internal sealed record ArtistList([property: JsonConverter(typeof(OneOrManyConverter<ChartArtist>))] IReadOnlyList<ChartArtist>? Artist);

internal sealed record ChartArtist(string Name, int Playcount, string Url);

internal sealed record TopTracksResponse([property: JsonRequired] TrackList TopTracks);

internal sealed record TrackList([property: JsonConverter(typeof(OneOrManyConverter<ChartTrack>))] IReadOnlyList<ChartTrack>? Track);

internal sealed record ChartTrack(string Name, int Playcount, string Url, NamedRef Artist, IReadOnlyList<Image>? Image);

internal sealed record NamedRef(string Name);

internal sealed record TopAlbumsResponse([property: JsonRequired] AlbumList TopAlbums);

internal sealed record AlbumList([property: JsonConverter(typeof(OneOrManyConverter<ChartAlbum>))] IReadOnlyList<ChartAlbum>? Album);

internal sealed record ChartAlbum(string Name, int Playcount, string Url, NamedRef Artist, IReadOnlyList<Image>? Image);

internal sealed record UserInfoResponse([property: JsonRequired] UserInfo User);

internal sealed record UserInfo(
    long Playcount,
    [property: JsonPropertyName("artist_count")] int ArtistCount,
    [property: JsonPropertyName("track_count")] int TrackCount,
    [property: JsonPropertyName("album_count")] int AlbumCount,
    Registration Registered);

internal sealed record Registration(long Unixtime);

internal sealed record ErrorResponse(int Error, string Message);

internal sealed class OneOrManyConverter<T> : JsonConverter<IReadOnlyList<T>>
{
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var itemType = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        if (reader.TokenType != JsonTokenType.StartArray)
            return [JsonSerializer.Deserialize(ref reader, itemType)!];

        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(JsonSerializer.Deserialize(ref reader, itemType)!);
        return items;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(RecentTracksResponse))]
[JsonSerializable(typeof(TopArtistsResponse))]
[JsonSerializable(typeof(ArtistChartResponse))]
[JsonSerializable(typeof(TopTracksResponse))]
[JsonSerializable(typeof(TopAlbumsResponse))]
[JsonSerializable(typeof(UserInfoResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(RecentTrack))]
[JsonSerializable(typeof(ChartArtist))]
[JsonSerializable(typeof(ChartTrack))]
[JsonSerializable(typeof(ChartAlbum))]
internal sealed partial class LastFmJson : JsonSerializerContext;
