using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Queries;

/// <summary>
/// Query to get a permission by ID.
/// </summary>
public record GetPermissionByIdQuery(Guid Id) : IRequest<ErrorOr<PermissionDto>>;
