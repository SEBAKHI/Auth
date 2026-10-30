namespace Auth_API.Modules.MachineCredentialChecks.Contracts;

/// <summary>
/// An API key presented to the calling application, to validate (OI-128 PR B).
/// </summary>
public record ValidateApiKeyRequest(string ApiKey);

/// <summary>
/// A webhook key presented to the calling application, to validate (OI-128 PR B).
/// </summary>
public record ValidateWebhookKeyRequest(string WebhookKey);
