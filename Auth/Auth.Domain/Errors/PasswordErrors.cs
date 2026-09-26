using Auth.Domain.Constants;
using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on a password the request carries: the request-shape rules the validators declare, and the policy PasswordValidator applies.
/// </summary>
public static class PasswordErrors
{
    public static readonly Error ConfirmationMismatch = Error.Validation(
        code: "Password.ConfirmationMismatch",
        description: "The password confirmation does not match the new password.");

    public static readonly Error CurrentRequired = Error.Validation(
        code: "Password.CurrentRequired",
        description: "Current password is required.");

    public static readonly Error CurrentTooLong = Error.Validation(
        code: "Password.CurrentTooLong",
        description: "Password must not exceed 128 characters.");

    public static readonly Error NewMustDiffer = Error.Validation(
        code: "Password.NewMustDiffer",
        description: "New password must be different from the current password.");

    public static readonly Error NewRequired = Error.Validation(
        code: "Password.NewRequired",
        description: "New password is required.");

    public static readonly Error NewTooLong = Error.Validation(
        code: "Password.NewTooLong",
        description: "Password must not exceed 128 characters.");

    public static readonly Error Required = Error.Validation(
        code: "Password.Required",
        description: "Password is required.");

    public static readonly Error TooLong = Error.Validation(
        code: "Password.TooLong",
        description: "Password must not exceed 128 characters.");

    // The password policy, applied by PasswordValidator in the handler after the
    // request-shape rules above. The field it concerns differs by use case
    // (Password on sign-up, NewPassword on change and reset), so each rule takes
    // the property it is checking and carries it for the mapper.
    public static Error TooShort(int minimumLength, string property) => Error.Validation(
        code: "Password.TooShort",
        description: $"Password must be at least {minimumLength} characters long.",
        metadata: new()
        {
            [ErrorMetadataKeys.Args] = new object[] { minimumLength },
            [ErrorMetadataKeys.Property] = property,
        });

    public static Error RequiresUppercase(string property) => Policy(
        "Password.RequiresUppercase", "Password must contain at least one uppercase letter.", property);

    public static Error RequiresLowercase(string property) => Policy(
        "Password.RequiresLowercase", "Password must contain at least one lowercase letter.", property);

    public static Error RequiresDigit(string property) => Policy(
        "Password.RequiresDigit", "Password must contain at least one digit.", property);

    public static Error RequiresSpecialCharacter(string property) => Policy(
        "Password.RequiresSpecialCharacter", "Password must contain at least one special character.", property);

    public static Error CommonPattern(string property) => Policy(
        "Password.CommonPattern", "Password contains a common pattern that is easy to guess.", property);

    private static Error Policy(string code, string description, string property) => Error.Validation(
        code: code,
        description: description,
        metadata: new() { [ErrorMetadataKeys.Property] = property });
}
