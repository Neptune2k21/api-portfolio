using Microsoft.AspNetCore.Http.HttpResults;

namespace Portfolio.Api.Common;

public static class Validate
{
    /// <summary>Renvoie un 400 qui liste toutes les règles non respectées, ou null si la requête est valide.</summary>
    public static ValidationProblem? Query(params ReadOnlySpan<(bool Valid, string Field, string Message)> rules)
    {
        Dictionary<string, string[]>? errors = null;
        foreach (var (valid, field, message) in rules)
        {
            if (!valid)
                (errors ??= [])[field] = [message];
        }

        return errors is null ? null : TypedResults.ValidationProblem(errors);
    }
}
