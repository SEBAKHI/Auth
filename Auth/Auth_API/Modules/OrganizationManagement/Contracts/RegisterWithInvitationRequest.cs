namespace Auth_API.Modules.OrganizationManagement.Contracts;

/// <summary>
/// Request to register a new account through an organization invitation.
/// The email is taken from the invitation identified by the route token.
/// </summary>
/// <param name="Password">The new account's password.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="PreferredLanguage">Optional preferred language code (defaults to "en").</param>
/// <param name="TimeZone">Optional time zone (defaults to "UTC").</param>
public record RegisterWithInvitationRequest(
    string Password,
    string FirstName,
    string LastName,
    string? PreferredLanguage = null,
    string? TimeZone = null);
