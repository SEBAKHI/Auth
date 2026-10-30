namespace Auth.Sdk.Models;

/// <summary>What a credential check concluded.</summary>
public enum CredentialCheckStatus
{
    /// <summary>The credential is one of this application's, and active.</summary>
    Valid,

    /// <summary>The AuthSystem said the credential is not valid for this application, or it is malformed.</summary>
    Invalid,

    /// <summary>
    /// No answer could be had: the AuthSystem was throttling, failing or unreachable, or refused this
    /// application's machine credential. Says nothing about the credential itself; try again later.
    /// </summary>
    Unavailable,
}

/// <summary>
/// The outcome of validating an API key or a webhook key (SDK 2.0). "Invalid" and "could not
/// tell" are different answers: the first is a 401 for the caller, the second a 503, and only the
/// first may be cached.
/// </summary>
public sealed class CredentialCheck<TResult> where TResult : class
{
    private CredentialCheck(CredentialCheckStatus status, TResult? result, TimeSpan? retryAfter)
    {
        Status = status;
        Result = result;
        RetryAfter = retryAfter;
    }

    public CredentialCheckStatus Status { get; }

    /// <summary>The validated credential's metadata; set only when <see cref="Status"/> is <see cref="CredentialCheckStatus.Valid"/>.</summary>
    public TResult? Result { get; }

    /// <summary>When to try again, for <see cref="CredentialCheckStatus.Unavailable"/>.</summary>
    public TimeSpan? RetryAfter { get; }

    public static CredentialCheck<TResult> Valid(TResult result) => new(CredentialCheckStatus.Valid, result, null);

    public static CredentialCheck<TResult> Invalid() => new(CredentialCheckStatus.Invalid, null, null);

    public static CredentialCheck<TResult> Unavailable(TimeSpan retryAfter) => new(CredentialCheckStatus.Unavailable, null, retryAfter);
}
