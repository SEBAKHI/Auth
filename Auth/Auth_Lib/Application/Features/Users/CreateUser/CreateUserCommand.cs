using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Users.CreateUser;

/// <summary>
/// Command to create a new user.
/// </summary>
public record CreateUserCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null,
    IReadOnlyList<Guid>? RoleIds = null) : IRequest<ErrorOr<UserDto>>
{
    /// <summary>
    /// The ID of the user creating this account (for audit).
    /// </summary>
    public Guid CreatedBy { get; set; }
}
