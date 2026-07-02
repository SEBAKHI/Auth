using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissions;

/// <summary>
/// Query to get permissions for an application.
/// </summary>
public record GetPermissionsQuery(
    Guid? ApplicationId = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
