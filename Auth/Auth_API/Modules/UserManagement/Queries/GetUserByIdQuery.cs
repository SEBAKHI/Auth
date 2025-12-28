using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Queries;

/// <summary>
/// Query to get a user by ID.
/// </summary>
public record GetUserByIdQuery(Guid Id) : IRequest<ErrorOr<UserDto>>;
