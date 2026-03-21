namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record UpdateApplicationRequest(
    string? SubscriptionTier = null,
    DateTime? ExpiresAt = null,
    bool? IsActive = null);
