namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request body for importing caller-supplied key material behind a verified
/// step-up confirmation.
/// </summary>
public record ImportSecretRequest
{
    /// <summary>
    /// The key material to store. Must be byte-for-byte what the confirmation
    /// was raised against — the approval is bound to a digest of it.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// The verified step-up confirmation authorizing this import.
    /// </summary>
    public Guid ChallengeId { get; init; }
}
