using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Portfolio.Api.Common;

public sealed class ApiKeyOptions
{
    public const string Section = "Api";

    public string? Key { get; set; }
}

/// <summary>
/// L'API n'est appelée que côté serveur par le front du portfolio, qui envoie la clé partagée dans <see cref="Header"/>.
/// Sans clé configurée (en Development uniquement, cf. validation), tout passe.
/// </summary>
internal sealed class ApiKeyFilter(IOptions<ApiKeyOptions> options) : IEndpointFilter
{
    public const string Header = "X-Api-Key";

    private readonly byte[]? _expected = options.Value.Key is { Length: > 0 } key ? Encoding.UTF8.GetBytes(key) : null;

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (_expected is null)
            return next(context);

        var provided = Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[Header].ToString());
        return CryptographicOperations.FixedTimeEquals(provided, _expected)
            ? next(context)
            : ValueTask.FromResult<object?>(TypedResults.Unauthorized());
    }
}

public static class ApiKeyExtensions
{
    public static IServiceCollection AddApiKey(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddOptions<ApiKeyOptions>()
            .Bind(config.GetSection(ApiKeyOptions.Section))
            .Validate(o => env.IsDevelopment() || !string.IsNullOrWhiteSpace(o.Key), "Api:Key is required outside Development.")
            .ValidateOnStart();
        return services;
    }

    public static RouteGroupBuilder RequireApiKey(this RouteGroupBuilder group) => group.AddEndpointFilter<ApiKeyFilter>();
}
