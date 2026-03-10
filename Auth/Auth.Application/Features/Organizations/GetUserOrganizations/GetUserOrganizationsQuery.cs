using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetUserOrganizations;

/// <summary>
/// Query to get all organizations the user is a member of.
/// </summary>
public record GetUserOrganizationsQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<OrganizationSummaryDto>>>;
