using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserById;

/// <summary>
/// Query to get a user by ID.
/// </summary>
public record GetUserByIdQuery(Guid Id) : IRequest<ErrorOr<UserDto>>
{
    /// <summary>
    /// The caller, for the account's <see cref="UserDto.AllowedActions"/>; null computes none, which a client reads as
    /// allowed.
    /// </summary>
    public Guid? ActorId { get; init; }
}
