using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserApplications;

/// <summary>
/// Query to get all applications a user has access to.
/// </summary>
public record GetUserApplicationsQuery(
    Guid UserId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<UserApplicationDto>>>;
