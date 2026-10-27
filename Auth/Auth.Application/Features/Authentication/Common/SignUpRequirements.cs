using Auth.Application.Configuration;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// What a new account must give when it signs up (OI-215), through either door that
/// creates one for a stranger: the public sign-up and an invitation. The one reading
/// of the two Registration switches, so the completion steps, the code sign-in's
/// next step and the public sign-in options can never disagree.
/// </summary>
/// <remarks>
/// <para>
/// The password switch is honoured only while <c>Email:Enabled</c> is on. An account
/// without a password signs in with a code emailed to it, and with email off nothing
/// could deliver that code: such an account could not sign in at all, so the sign-up
/// asks for a password whatever the switch says. The console refuses to save that
/// combination; this is the run-time half, for a value that arrived some other way.
/// </para>
/// <para>
/// A field sent when it is not required is still validated and kept: an API client
/// that sends a password keeps getting an account with one.
/// </para>
/// </remarks>
/// <param name="PasswordRequired">Whether the sign-up must set a password.</param>
/// <param name="NameRequired">Whether the sign-up must give a first and last name.</param>
public sealed record SignUpRequirements(bool PasswordRequired, bool NameRequired)
{
    /// <summary>The requirements the current settings impose.</summary>
    public static SignUpRequirements From(RegistrationSettings registration, EmailSettings email) =>
        new(
            PasswordRequired: registration.PasswordAtSignUp || !email.Enabled,
            NameRequired: registration.NameAtSignUp);

    /// <summary>
    /// The required fields a sign-up left out, each as the validation pipeline reports
    /// a missing field: its catalog code, with the property it names in metadata.
    /// Empty when nothing required is missing.
    /// </summary>
    /// <param name="password">The password sent, if any.</param>
    /// <param name="firstName">The first name sent, if any.</param>
    /// <param name="lastName">The last name sent, if any.</param>
    public List<Error> Missing(string? password, string? firstName, string? lastName)
    {
        var errors = new List<Error>();

        if (PasswordRequired && string.IsNullOrWhiteSpace(password))
        {
            errors.Add(MissingField(PasswordErrors.Required, "Password"));
        }

        if (NameRequired && string.IsNullOrWhiteSpace(firstName))
        {
            errors.Add(MissingField(UserErrors.FirstNameRequired, "FirstName"));
        }

        if (NameRequired && string.IsNullOrWhiteSpace(lastName))
        {
            errors.Add(MissingField(UserErrors.LastNameRequired, "LastName"));
        }

        return errors;
    }

    /// <summary>Whether the sign-up sent a password to set: one that is not blank.</summary>
    public static bool Sent(string? password) => !string.IsNullOrWhiteSpace(password);

    private static Error MissingField(Error error, string property) =>
        Error.Validation(
            code: error.Code,
            description: error.Description,
            metadata: new Dictionary<string, object> { [ErrorMetadataKeys.Property] = property });
}
