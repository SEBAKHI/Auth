using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// An organization, read by an application as one of its members (OI-194). Membership alone is
/// enough: no permission is required to see the organization one belongs to.
/// </summary>
public sealed record GetApplicationOrganizationQuery(ApplicationDirectoryCaller Caller, Guid OrganizationId)
    : IRequest<ErrorOr<ApplicationOrganizationResponse>>;
