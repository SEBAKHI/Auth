namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record UpdateOrganizationRequest(
    string Name,
    string ContactEmail,
    string? Description = null,
    string? LogoUrl = null,
    string? Website = null,
    bool? IsActive = null);
