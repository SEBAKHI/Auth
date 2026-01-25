using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Queries;

/// <summary>
/// Query to get all organizations the user is a member of.
/// </summary>
public record GetUserOrganizationsQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<OrganizationSummaryDto>>>;
