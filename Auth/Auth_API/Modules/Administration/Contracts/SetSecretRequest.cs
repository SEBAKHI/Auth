namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request for setting a secret value.
/// </summary>
public record SetSecretRequest
{
    /// <summary>
    /// The secret value to store.
    /// </summary>
    public string Value { get; init; } = string.Empty;
}
