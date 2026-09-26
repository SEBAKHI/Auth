using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// The one <c>CustomizeProblemDetails</c> (ADR 0001, section 2). It runs in every
/// <c>IProblemDetailsService</c> write and in MVC's <c>ProblemDetailsFactory</c>, after the
/// framework has set <c>type</c>, <c>title</c>, <c>status</c> and <c>traceId</c>, which it
/// never changes.
/// </summary>
internal static class ProblemCustomization
{
    public static void Apply(ProblemDetailsContext context, ProblemText text, OutageOptions outage)
    {
        var http = context.HttpContext;

        // Minimal APIs write validation problems with a dictionary-shaped `errors`: a second
        // shape. The writer serializes context.ProblemDetails after this callback, so a plain
        // problem replaces it. (MVC ignores the replacement; its model-state 400 goes through
        // the factory without `errors` instead.)
        if (context.ProblemDetails is HttpValidationProblemDetails validation)
        {
            context.ProblemDetails = WithoutErrors(validation);
        }

        var problem = context.ProblemDetails;
        problem.Instance ??= http.Request.Path;

        // The only author of `code`: a code a library wrote itself is replaced too.
        var code = http.Items.TryGetValue(ProblemItems.Code, out var recorded) && recorded is string { Length: > 0 } reason
            ? reason
            : TransportErrorCodes.For(problem.Status);
        problem.Extensions["code"] = code;

        var args = http.Items.TryGetValue(ProblemItems.Args, out var recordedArgs) ? recordedArgs as object[] : null;
        problem.Detail = text.Describe(code, args);
        if (problem.Detail is not null)
        {
            http.Response.Headers.ContentLanguage = CultureInfo.CurrentUICulture.Name;
        }

        if (problem.Status == StatusCodes.Status503ServiceUnavailable
            && !http.Response.Headers.ContainsKey(HeaderNames.RetryAfter))
        {
            http.Response.Headers.RetryAfter = outage.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static ProblemDetails WithoutErrors(HttpValidationProblemDetails validation)
    {
        var plain = new ProblemDetails
        {
            Type = validation.Type,
            Title = validation.Title,
            Status = validation.Status,
            Instance = validation.Instance,
        };

        foreach (var (key, value) in validation.Extensions)
        {
            plain.Extensions[key] = value;
        }

        return plain;
    }
}
