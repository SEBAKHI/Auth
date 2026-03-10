using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissionById;

/// <summary>
/// Query to get a permission by ID.
/// </summary>
public record GetPermissionByIdQuery(Guid Id) : IRequest<ErrorOr<PermissionDto>>;
