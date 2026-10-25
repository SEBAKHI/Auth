using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserPermissions;

/// <summary>
/// Query to get all direct permissions granted to a user.
/// </summary>
public record GetUserPermissionsQuery(
    Guid UserId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<UserPermissionDto>>>
{
    /// <summary>
    /// The caller, for each grant's <see cref="UserPermissionDto.CanRemove"/>; null computes none, which a client reads as
    /// allowed.
    /// </summary>
    public Guid? ActorId { get; init; }
}
