namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Response for RSA key generation.
/// </summary>
public record RsaKeyGenerationResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The public key in PEM format for external token validation.
    /// </summary>
    public string PublicKeyPem { get; init; } = string.Empty;
}
