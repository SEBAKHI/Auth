namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record EnableApplicationRequest(
    Guid ApplicationId,
    string? SubscriptionTier = null,
    DateTime? ExpiresAt = null);
