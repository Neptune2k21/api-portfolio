namespace Portfolio.Api.Common;

/// <summary>Un service tiers dont dépend l'API a échoué ou renvoyé une réponse inexploitable.</summary>
public sealed class UpstreamException(string service, string message, Exception? inner = null)
    : Exception($"{service}: {message}", inner)
{
    public string Service { get; } = service;
}
