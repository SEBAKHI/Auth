using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the contact email of an organization or an application.
/// </summary>
public static class ContactEmailErrors
{
    public static readonly Error InvalidFormat = Error.Validation(
        code: "ContactEmail.InvalidFormat",
        description: "A valid contact email address is required.");

    public static readonly Error Required = Error.Validation(
        code: "ContactEmail.Required",
        description: "Contact email is required.");

    public static readonly Error TooLong = Error.Validation(
        code: "ContactEmail.TooLong",
        description: "Contact email must not exceed 254 characters.");
}
