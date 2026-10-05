namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// The organization-creation step: <c>clientId</c> and either <c>name</c>
/// (create an organization) or <c>organizationId</c> (set up one the user
/// already owns). When both are sent, <c>organizationId</c> wins.
/// </summary>
public record SetUpOrganizationRequest
{
    public string? ClientId { get; init; }

    public string? Name { get; init; }

    public Guid? OrganizationId { get; init; }
}

/// <summary>The organization now set up for the application.</summary>
public record SetUpOrganizationResponse(Guid OrganizationId);
