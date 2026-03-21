namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record CreateOrganizationRequest(
    string Code,
    string Name,
    string ContactEmail,
    string? Description = null,
    string? LogoUrl = null,
    string? Website = null);
