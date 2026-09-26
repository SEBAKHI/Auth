using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;

namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// The codes of the errors the framework produces rather than a handler: one per status
/// (ADR 0001). Published in <c>docs/api/error-codes.json</c> like every catalog code.
/// </summary>
public static class TransportErrorCodes
{
    public const string BadRequest = "Http.BadRequest";
    public const string Unauthenticated = "Http.Unauthenticated";
    public const string Forbidden = "Http.Forbidden";
    public const string NotFound = "Http.NotFound";
    public const string MethodNotAllowed = "Http.MethodNotAllowed";
    public const string ContentTooLarge = "Http.ContentTooLarge";
    public const string UnsupportedMediaType = "Http.UnsupportedMediaType";
    public const string RateLimited = "Http.RateLimited";
    public const string Unavailable = "Http.Unavailable";
    public const string Unexpected = "Http.Unexpected";

    private static readonly FrozenDictionary<int, string> ByStatus = new Dictionary<int, string>
    {
        [StatusCodes.Status400BadRequest] = BadRequest,
        [StatusCodes.Status401Unauthorized] = Unauthenticated,
        [StatusCodes.Status403Forbidden] = Forbidden,
        [StatusCodes.Status404NotFound] = NotFound,
        [StatusCodes.Status405MethodNotAllowed] = MethodNotAllowed,
        [StatusCodes.Status413PayloadTooLarge] = ContentTooLarge,
        [StatusCodes.Status415UnsupportedMediaType] = UnsupportedMediaType,
        [StatusCodes.Status429TooManyRequests] = RateLimited,
        [StatusCodes.Status500InternalServerError] = Unexpected,
        [StatusCodes.Status502BadGateway] = Unavailable,
        [StatusCodes.Status503ServiceUnavailable] = Unavailable,
        [StatusCodes.Status504GatewayTimeout] = Unavailable,
    }.ToFrozenDictionary();

    /// <summary>Every transport code, for the contract test.</summary>
    public static IReadOnlyCollection<string> All { get; } = ByStatus.Values.Distinct().ToArray();

    /// <summary>
    /// The code for a framework-produced status. Any other 4xx takes <see cref="BadRequest"/>,
    /// any other status <see cref="Unexpected"/>.
    /// </summary>
    public static string For(int? status) =>
        status is int value && ByStatus.TryGetValue(value, out var code) ? code
        : status is >= 400 and < 500 ? BadRequest
        : Unexpected;
}
