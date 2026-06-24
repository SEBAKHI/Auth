namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record UpdateOrganizationApplicationRequest(
    string? SubscriptionTier = null,
    DateTime? ExpiresAt = null,
    bool? IsActive = null);
