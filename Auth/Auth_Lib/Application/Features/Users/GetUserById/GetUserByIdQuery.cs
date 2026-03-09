using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Users.GetUserById;

/// <summary>
/// Query to get a user by ID.
/// </summary>
public record GetUserByIdQuery(Guid Id) : IRequest<ErrorOr<UserDto>>;
