namespace Auth_API.Modules.UserManagement.Contracts;

public record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null,
    string? Theme = null,
    IReadOnlyList<Guid>? RoleIds = null);
