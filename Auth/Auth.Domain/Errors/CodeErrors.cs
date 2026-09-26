using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the identifier code of an application or a role.
/// </summary>
public static class CodeErrors
{
    public static readonly Error InvalidFormat = Error.Validation(
        code: "Code.InvalidFormat",
        description: "Code must contain only alphanumeric characters, dots, hyphens, and underscores.");

    public static readonly Error Required = Error.Validation(
        code: "Code.Required",
        description: "Code is required.");

    public static readonly Error TooLong = Error.Validation(
        code: "Code.TooLong",
        description: "Code must not exceed 100 characters.");
}
