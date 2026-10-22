using Auth.Domain.Enums;

namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to raise a step-up confirmation for a destructive secret operation.
/// </summary>
public record SecretOperationChallengeRequest
{
    /// <summary>
    /// The operation the resulting approval will authorize, and nothing else.
    /// </summary>
    public SecretOperation Operation { get; init; }

    /// <summary>
    /// The key material for the import operations, so the confirmation is bound
    /// to the exact bytes being approved. Omit for the generate operations.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// For a change of the HMAC keyring, which one (S11): the approval is then bound to
    /// that action and to the key it applies to. Omit for every other operation.
    /// </summary>
    public KeyringAction? KeyringAction { get; init; }

    /// <summary>The key a reinstate or a retire names; omit for every other action.</summary>
    public int? KeyId { get; init; }
}
