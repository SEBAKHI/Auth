namespace Auth_API.Modules.UserManagement.Contracts;

public record UpdateProfileRequest(
    string? FirstName = null,
    string? LastName = null,
    string? PhoneNumber = null,
    string? PreferredLanguage = null,
    string? TimeZone = null,
    string? Theme = null);
