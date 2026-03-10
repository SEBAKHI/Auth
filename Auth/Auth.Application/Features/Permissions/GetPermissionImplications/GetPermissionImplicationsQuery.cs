using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissionImplications;

/// <summary>
/// Query to get all permissions implied by a permission.
/// </summary>
public record GetPermissionImplicationsQuery(Guid PermissionId) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
