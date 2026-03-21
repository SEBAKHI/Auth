namespace Auth_API.Modules.ApiKeyManagement.Contracts;

public record RevokeApiKeyRequest(string? Reason = null);
