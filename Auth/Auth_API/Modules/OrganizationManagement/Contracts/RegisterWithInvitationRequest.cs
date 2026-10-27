namespace Auth_API.Modules.OrganizationManagement.Contracts;

/// <summary>
/// Request to register a new account through an organization invitation.
/// The email is taken from the invitation identified by the route token.
/// </summary>
/// <param name="Password">
/// The new account's password: required unless <c>Registration:PasswordAtSignUp</c> is
/// off and email is on (OI-215). Without one the account signs in by emailed code.
/// </param>
/// <param name="FirstName">First name: required unless <c>Registration:NameAtSignUp</c> is off.</param>
/// <param name="LastName">Last name: required unless <c>Registration:NameAtSignUp</c> is off.</param>
/// <param name="PreferredLanguage">Optional preferred language code (defaults to "en").</param>
/// <param name="TimeZone">Optional time zone (defaults to "UTC").</param>
public record RegisterWithInvitationRequest(
    string? Password = null,
    string? FirstName = null,
    string? LastName = null,
    string? PreferredLanguage = null,
    string? TimeZone = null);
