using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoles;

/// <summary>
/// Query to get roles for an application.
/// </summary>
public record GetRolesQuery(
    Guid? ApplicationId = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>
{
    /// <summary>
    /// The caller, for each item's <see cref="RoleDto.CanGrant"/>; null computes none,
    /// which a client reads as every item grantable.
    /// </summary>
    public Guid? ActorId { get; init; }
}
