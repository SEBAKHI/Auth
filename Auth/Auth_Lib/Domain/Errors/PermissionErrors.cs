using ErrorOr;

namespace Auth_Lib.Domain.Errors;

/// <summary>
/// Domain errors related to permission operations.
/// </summary>
public static class PermissionErrors
{
    public static Error NotFound(Guid permissionId) => Error.NotFound(
        code: "Permission.NotFound",
        description: $"Permission with ID '{permissionId}' was not found.");

    public static Error NotFoundByCode(string code) => Error.NotFound(
        code: "Permission.NotFoundByCode",
        description: $"Permission with code '{code}' was not found.");

    public static Error DuplicateCode(string code, Guid applicationId) => Error.Conflict(
        code: "Permission.DuplicateCode",
        description: $"A permission with code '{code}' already exists in this application.");

    public static Error InvalidCode(string code) => Error.Validation(
        code: "Permission.InvalidCode",
        description: $"Permission code '{code}' is invalid. Use colon-separated hierarchy (e.g., 'module:resource:action').");

    public static Error CannotDeleteSystemPermission => Error.Forbidden(
        code: "Permission.CannotDeleteSystemPermission",
        description: "System permissions cannot be deleted.");

    public static Error CannotModifySystemPermission => Error.Forbidden(
        code: "Permission.CannotModifySystemPermission",
        description: "System permissions cannot be modified.");

    public static Error PermissionInactive => Error.Forbidden(
        code: "Permission.Inactive",
        description: "This permission is currently inactive.");

    public static Error PermissionAlreadyGranted => Error.Conflict(
        code: "Permission.AlreadyGranted",
        description: "This permission is already granted.");

    public static Error PermissionNotGranted => Error.NotFound(
        code: "Permission.NotGranted",
        description: "This permission is not granted.");

    public static Error CircularImplication => Error.Validation(
        code: "Permission.CircularImplication",
        description: "This would create a circular permission implication.");

    public static Error CannotGrantHigherPermission => Error.Forbidden(
        code: "Permission.CannotGrantHigher",
        description: "You cannot grant permissions that you do not have.");
}
