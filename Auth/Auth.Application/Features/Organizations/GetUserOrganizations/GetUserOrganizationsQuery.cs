using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetUserOrganizations;

/// <summary>
/// Query to get all organizations the user is a member of.
/// </summary>
public record GetUserOrganizationsQuery(
    Guid UserId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<OrganizationSummaryDto>>>;
