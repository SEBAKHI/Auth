namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request body for promoting an HMAC key (S11).
/// </summary>
public record KeyringPromoteRequest
{
    /// <summary>The verified step-up confirmation authorizing this change.</summary>
    public Guid ChallengeId { get; init; }

    /// <summary>
    /// True after a leak: a new key issues at once and every older key stops verifying
    /// now, except for webhook keys, which keep a 90-day grace. False to promote the
    /// staged key with the overlap window.
    /// </summary>
    public bool Emergency { get; init; }
}
