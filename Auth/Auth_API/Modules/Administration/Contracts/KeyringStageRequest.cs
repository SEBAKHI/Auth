namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request body for staging an HMAC key (S11).
/// </summary>
public record KeyringStageRequest
{
    /// <summary>The verified step-up confirmation authorizing this change.</summary>
    public Guid ChallengeId { get; init; }

    /// <summary>
    /// A key you supply (base64, at least 256 bits), which the confirmation was bound to;
    /// omit to have the server generate one.
    /// </summary>
    public string? Value { get; init; }
}
