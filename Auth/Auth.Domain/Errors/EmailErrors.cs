using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on an email address the request carries.
/// </summary>
public static class EmailErrors
{
    public static readonly Error InvalidFormat = Error.Validation(
        code: "Email.InvalidFormat",
        description: "A valid email address is required.");

    public static readonly Error Required = Error.Validation(
        code: "Email.Required",
        description: "Email is required.");

    public static readonly Error TooLong = Error.Validation(
        code: "Email.TooLong",
        description: "Email must not exceed 254 characters.");
}
