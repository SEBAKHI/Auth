namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Response for importing caller-supplied key material (bring-your-own-keys).
/// </summary>
public record KeyImportResponse
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
    /// The public key in PEM format derived from the imported RSA private key.
    /// Populated for the RSA import endpoint only; <c>null</c> otherwise.
    /// </summary>
    public string? PublicKeyPem { get; init; }
}
