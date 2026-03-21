namespace Auth_API.Modules.ApiKeyManagement.Contracts;

/// <summary>
/// Request to validate an API key.
/// </summary>
public record ValidateApiKeyRequest(string ApiKey);
