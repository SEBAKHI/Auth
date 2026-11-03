using Auth.Domain.Enums;

namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to answer a step-up confirmation with the emailed code.
/// </summary>
public record VerifySecretOperationChallengeRequest
{
    /// <summary>
    /// The six-digit code from the confirmation email.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// For a change of a keyring (S11, S12), the action the confirmation was raised for,
    /// so its live cost is returned; checked against the confirmation. Omit otherwise.
    /// </summary>
    public KeyringAction? KeyringAction { get; init; }

    /// <summary>The HMAC key a reinstate or a retire names.</summary>
    public int? KeyId { get; init; }

    /// <summary>The signing key (its kid) a retire names (S12).</summary>
    public string? Kid { get; init; }

    /// <summary>The key material of a staged import.</summary>
    public string? Value { get; init; }
}
