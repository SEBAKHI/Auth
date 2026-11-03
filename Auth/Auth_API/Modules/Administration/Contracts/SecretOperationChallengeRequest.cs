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
    /// For a change of a keyring, which one: the HMAC keyring with the HMAC operations (S11),
    /// the signing keyring with the RSA operations (S12). The approval is then bound to that
    /// ring, that action and the key it applies to. Omit for every other operation.
    /// </summary>
    public KeyringAction? KeyringAction { get; init; }

    /// <summary>The HMAC key a reinstate or a retire names; omit for every other action.</summary>
    public int? KeyId { get; init; }

    /// <summary>The signing key (its kid) a retire names (S12); omit for every other action.</summary>
    public string? Kid { get; init; }
}
