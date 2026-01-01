using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Command for authenticated users to update their own profile.
/// This is separate from UpdateUserCommand as it doesn't require admin permissions
/// and is limited to self-service profile fields.
/// </summary>
public record UpdateProfileCommand(
    Guid UserId,
    string? FirstName = null,
    string? LastName = null,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null) : IRequest<ErrorOr<UserDto>>;
