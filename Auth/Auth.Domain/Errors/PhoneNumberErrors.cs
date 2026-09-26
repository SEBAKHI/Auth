using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on a phone number.
/// </summary>
public static class PhoneNumberErrors
{
    public static readonly Error InvalidFormat = Error.Validation(
        code: "PhoneNumber.InvalidFormat",
        description: "Phone number format is invalid.");

    public static readonly Error Required = Error.Validation(
        code: "PhoneNumber.Required",
        description: "Phone number is required.");

    public static readonly Error TooFewDigits = Error.Validation(
        code: "PhoneNumber.TooFewDigits",
        description: "Phone number must contain at least 7 digits.");

    public static readonly Error TooLong = Error.Validation(
        code: "PhoneNumber.TooLong",
        description: "Phone number must not exceed 20 characters.");
}
