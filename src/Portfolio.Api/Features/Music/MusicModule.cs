using System.Text.Json;
using System.Text.Json.Serialization;
using Portfolio.Api.Features.Music.Artwork;
using Portfolio.Api.Features.Music.LastFm;

namespace Portfolio.Api.Features.Music;

public static class MusicModule
{
    public static IServiceCollection AddMusic(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<LastFmOptions>()
            .Bind(config.GetSection(LastFmOptions.Section))
            .Validate(o => o.ApiKey.Length > 0 && o.User.Length > 0, "LastFm:ApiKey et LastFm:User sont obligatoires.")
            .ValidateOnStart();

        services.AddOptions<MusicOptions>()
            .Bind(config.GetSection(MusicOptions.Section))
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Music:TimeZone est inconnu (tzdata absent ?).")
            .ValidateOnStart();

        services.AddHttpClient<ILastFmClient, LastFmClient>(http => http.BaseAddress = new Uri(LastFmClient.BaseUrl))
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            });

        services.AddHttpClient<IArtworkSource, DeezerClient>(http => http.BaseAddress = new Uri(DeezerClient.BaseUrl))
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
            });

        services.AddScoped<MusicService>();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Add(MusicJson.Default));
        return services;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Scrobble))]
[JsonSerializable(typeof(IReadOnlyList<Scrobble>))]
[JsonSerializable(typeof(ListenerProfile))]
[JsonSerializable(typeof(IReadOnlyList<ArtistPlays>))]
[JsonSerializable(typeof(IReadOnlyList<TrackPlays>))]
[JsonSerializable(typeof(IReadOnlyList<AlbumPlays>))]
[JsonSerializable(typeof(ListeningActivity))]
[JsonSerializable(typeof(Soundtrack))]
internal sealed partial class MusicJson : JsonSerializerContext;
