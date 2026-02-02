using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Queries;

/// <summary>
/// Query to get all permissions implied by a permission.
/// </summary>
public record GetPermissionImplicationsQuery(Guid PermissionId) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
