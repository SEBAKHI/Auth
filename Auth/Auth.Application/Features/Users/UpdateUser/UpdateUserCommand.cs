using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.UpdateUser;

/// <summary>
/// Command to update an existing user.
/// </summary>
public record UpdateUserCommand(
    Guid Id,
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null,
    string? Theme = null) : IRequest<ErrorOr<UserDto>>
{
    /// <summary>
    /// The ID of the user performing the update (for audit).
    /// </summary>
    public Guid ModifiedBy { get; init; }
}
