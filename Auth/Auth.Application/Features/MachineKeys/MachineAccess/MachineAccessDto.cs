namespace Auth.Application.Features.MachineKeys.MachineAccess;

/// <summary>
/// What an application's machine key may be granted beyond the default machine scopes (OI-202):
/// the administrator's consent to each consent-only machine scope.
/// </summary>
public sealed record MachineAccessDto
{
    /// <summary>
    /// Whether the application's server, with its machine key and no user, may obtain
    /// <c>organizations.read.all</c>: read the directory of every organization that enabled it.
    /// </summary>
    public required bool OrganizationsReadAll { get; init; }
}
