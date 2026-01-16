namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Response for gateway token generation.
/// </summary>
public record GatewayTokenGenerationResponse
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
    /// The generated gateway token. Store this securely - it will not be shown again.
    /// </summary>
    public string Token { get; init; } = string.Empty;
}
