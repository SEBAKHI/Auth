using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationAccessGrants;

/// <summary>
/// Query for an application's access list — the users individually invited to
/// it. Meaningful for a restricted application; an open one admits everyone
/// regardless of what this returns.
/// </summary>
public record GetApplicationAccessGrantsQuery(
    Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<ApplicationAccessGrantDto>>>;
