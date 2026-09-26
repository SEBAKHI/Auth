using System.Collections.Frozen;
using ErrorOr;

namespace Auth_API.Common.Errors;

/// <summary>
/// The only ErrorType-to-status map in the solution (ADR 0001, section 6). The status of a
/// handler result is its first error's.
/// </summary>
public static class ErrorStatusMap
{
    private static readonly FrozenDictionary<int, int> Statuses = new Dictionary<int, int>
    {
        [(int)ErrorType.Validation] = StatusCodes.Status400BadRequest,
        [(int)ErrorType.Unauthorized] = StatusCodes.Status401Unauthorized,
        [(int)ErrorType.Forbidden] = StatusCodes.Status403Forbidden,
        [(int)ErrorType.NotFound] = StatusCodes.Status404NotFound,
        [(int)ErrorType.Conflict] = StatusCodes.Status409Conflict,
        [(int)ErrorType.Failure] = StatusCodes.Status500InternalServerError,
        [(int)ErrorType.Unexpected] = StatusCodes.Status500InternalServerError,
    }.ToFrozenDictionary();

    /// <summary>The error types the map declares, for the coverage test.</summary>
    public static IReadOnlyCollection<int> DeclaredTypes => Statuses.Keys;

    public static int ToStatusCode(Error error) =>
        Statuses.GetValueOrDefault(error.NumericType, StatusCodes.Status500InternalServerError);
}
