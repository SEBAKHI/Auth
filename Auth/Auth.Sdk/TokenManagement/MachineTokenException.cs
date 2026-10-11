using System.Net;

namespace Auth.Sdk.TokenManagement;

/// <summary>
/// The AuthSystem did not issue a machine access token.
/// </summary>
/// <remarks>
/// <see cref="StatusCode"/> tells the two cases apart. 400 (with the problem's <see cref="ErrorCode"/>,
/// usually <c>Auth.InvalidClient</c>) means the credential is wrong or revoked, and retrying will not
/// help. 429, 5xx, or null (no answer: a timeout or a network failure) is transient; retry later,
/// honouring <see cref="RetryAfter"/> when the server sent one.
/// </remarks>
public sealed class MachineTokenException : Exception
{
    public MachineTokenException(string message, HttpStatusCode? statusCode, string? errorCode, TimeSpan? retryAfter, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        RetryAfter = retryAfter;
    }

    /// <summary>The HTTP status of the answer; null when none arrived.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The <c>code</c> of the problem the AuthSystem answered with, when it sent one.</summary>
    public string? ErrorCode { get; }

    /// <summary>The <c>Retry-After</c> of a 429 or 503, when the server sent one.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>True for a failure that may pass on a later attempt: no answer, 408, 429 or 5xx.</summary>
    public bool IsTransient =>
        StatusCode is null
        || StatusCode == HttpStatusCode.RequestTimeout
        || StatusCode == HttpStatusCode.TooManyRequests
        || (int)StatusCode >= 500;
}
