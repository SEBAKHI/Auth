namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Response for HMAC key generation.
/// </summary>
public record HmacKeyGenerationResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
