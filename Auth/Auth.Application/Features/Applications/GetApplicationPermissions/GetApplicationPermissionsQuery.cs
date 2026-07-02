using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationPermissions;

/// <summary>
/// Query to get all permissions for an application.
/// </summary>
public record GetApplicationPermissionsQuery(
    Guid ApplicationId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
