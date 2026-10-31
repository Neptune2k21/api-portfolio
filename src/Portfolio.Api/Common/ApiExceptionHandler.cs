using Microsoft.AspNetCore.Diagnostics;

namespace Portfolio.Api.Common;

/// <summary>Transforme les erreurs attendues en réponses ProblemDetails ; le reste reste une 500.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "Requête invalide.", bad.Message),
            UpstreamException upstream => (StatusCodes.Status502BadGateway, $"{upstream.Service} est indisponible.", null),
            _ => (0, null, null),
        };
        if (status == 0)
            return false;

        if (exception is UpstreamException)
            logger.LogWarning(exception, "Échec d'un service tiers");

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }
}
