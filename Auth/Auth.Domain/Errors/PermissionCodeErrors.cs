using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on a permission code.
/// </summary>
public static class PermissionCodeErrors
{
    public static readonly Error Required = Error.Validation(
        code: "PermissionCode.Required",
        description: "Permission code is required.");

    public static readonly Error InvalidFormat = Error.Validation(
        code: "PermissionCode.InvalidFormat",
        description: "Permission code must contain only lowercase letters, digits, colons, hyphens, underscores, and asterisks.");

    public static readonly Error TooLong = Error.Validation(
        code: "PermissionCode.TooLong",
        description: "Code must not exceed 200 characters.");
}
