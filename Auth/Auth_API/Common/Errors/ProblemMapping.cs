using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Domain.Constants;
using Auth.Shared.Http.ErrorContract;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace Auth_API.Common.Errors;

/// <summary>
/// The single mapper from a handler's ErrorOr errors to the HTTP problem (ADR 0001). The status
/// comes from <see cref="ErrorStatusMap"/>; <c>code</c> and <c>detail</c> are written by the
/// problem-details customization from what this records; <c>errors</c> is added only for a
/// Validation result with two or more failures.
/// </summary>
public static class ProblemMapping
{
    /// <summary>The codes ErrorOr assigns when a factory call names none. Never emitted.</summary>
    public static readonly FrozenSet<string> ErrorOrDefaultCodes = new[]
    {
        "General.Failure", "General.Unexpected", "General.Validation", "General.Conflict",
        "General.NotFound", "General.Unauthorized", "General.Forbidden",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <param name="bodyType">
    /// The type the action binds from the request body, or <c>null</c> when it binds none: the
    /// members failures may point at.
    /// </param>
    public static ObjectResult ToProblem(
        HttpContext httpContext,
        ProblemDetailsFactory factory,
        IReadOnlyList<Error> errors,
        Type? bodyType)
    {
        foreach (var error in errors)
        {
            if (ErrorOrDefaultCodes.Contains(error.Code))
            {
                // A programming error: the exception handler answers 500 and logs it.
                throw new InvalidOperationException(
                    $"An error of type {error.Type} carries the ErrorOr default code {error.Code}; declare a catalog code.");
            }
        }

        var primary = errors[0];
        httpContext.Items[ProblemItems.Code] = primary.Code;
        if (primary.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Args) is object[] args)
        {
            httpContext.Items[ProblemItems.Args] = args;
        }

        var problem = factory.CreateProblemDetails(httpContext, ErrorStatusMap.ToStatusCode(primary));

        if (primary.Type == ErrorType.Validation && errors.Count >= 2)
        {
            var json = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
            problem.Extensions["errors"] = errors
                .Select(error => new ProblemErrorEntry(error.Code, PointerFor(error, bodyType, json)))
                .ToArray();
        }

        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        };
    }

    private static string? PointerFor(Error error, Type? bodyType, JsonSerializerOptions json) =>
        bodyType is not null
        && error.Metadata?.GetValueOrDefault(ErrorMetadataKeys.Property) is string { Length: > 0 } property
            ? JsonPointer.For(property, bodyType, json)
            : null;
}

/// <summary>One failure of a Validation result: its code, and the body member it concerns.</summary>
public sealed record ProblemErrorEntry(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pointer);
