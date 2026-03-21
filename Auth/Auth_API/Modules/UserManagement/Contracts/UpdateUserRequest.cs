namespace Auth_API.Modules.UserManagement.Contracts;

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string? DisplayName = null,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null);
