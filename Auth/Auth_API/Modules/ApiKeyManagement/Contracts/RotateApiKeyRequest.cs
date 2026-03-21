namespace Auth_API.Modules.ApiKeyManagement.Contracts;

public record RotateApiKeyRequest(int? GracePeriodMinutes = 60);
