using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetAvailableApplications;

/// <summary>
/// Query for the applications this organization is allowed to enable: switched
/// on, open to everyone, and not already enabled for it.
/// </summary>
/// <remarks>
/// Its own query rather than a filter over the platform-wide application list,
/// for two reasons. The console's application picker is shared with nine other
/// screens (roles, permissions, API keys, webhooks, audit-log filters,
/// notification templates) that legitimately need to see restricted
/// applications, so filtering it globally would break them. And reading the
/// platform-wide list needs <c>applications:read</c>, which an organization
/// administrator has no reason to hold — today they see an empty picker with no
/// explanation.
/// </remarks>
public record GetAvailableApplicationsQuery(
    Guid OrganizationId) : IRequest<ErrorOr<IReadOnlyList<AvailableApplicationDto>>>;
