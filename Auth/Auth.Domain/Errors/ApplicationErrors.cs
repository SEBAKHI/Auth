using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to application operations.
/// </summary>
public static class ApplicationErrors
{
    public static Error NotFound(Guid applicationId) => Error.NotFound(
        code: "Application.NotFound",
        description: $"Application with ID '{applicationId}' was not found.");

    public static Error NotFoundByCode(string code) => Error.NotFound(
        code: "Application.NotFoundByCode",
        description: $"Application with code '{code}' was not found.");

    public static Error DuplicateCode(string code) => Error.Conflict(
        code: "Application.DuplicateCode",
        description: $"An application with code '{code}' already exists.");

    public static Error CannotDeleteSystemApplication => Error.Forbidden(
        code: "Application.CannotDeleteSystem",
        description: "System applications cannot be deleted.");

    public static Error CannotModifySystemApplication => Error.Forbidden(
        code: "Application.CannotModifySystem",
        description: "System applications cannot be modified.");

    public static Error HasActiveApiKeys => Error.Conflict(
        code: "Application.HasActiveApiKeys",
        description: "Cannot delete application with active API keys. Revoke all API keys first.");

    public static Error HasActiveUsers => Error.Conflict(
        code: "Application.HasActiveUsers",
        description: "Cannot delete application with active user assignments.");

    public static Error HasActiveOrganizations => Error.Conflict(
        code: "Application.HasActiveOrganizations",
        description: "Cannot delete application that is enabled for organizations.");

    public static Error ApplicationInactive => Error.Forbidden(
        code: "Application.Inactive",
        description: "This application is currently inactive.");

    public static Error InvalidCode(string code) => Error.Validation(
        code: "Application.InvalidCode",
        description: $"Application code '{code}' is invalid. Use uppercase alphanumeric characters and underscores only.");

    public static Error CodeTooShort => Error.Validation(
        code: "Application.CodeTooShort",
        description: "Application code must be at least 2 characters long.");

    public static Error CodeTooLong => Error.Validation(
        code: "Application.CodeTooLong",
        description: "Application code cannot exceed 50 characters.");

    public static Error InvalidSessionTimeout => Error.Validation(
        code: "Application.InvalidSessionTimeout",
        description: "Session timeout must be between 1 and 1440 minutes (24 hours).");

    public static Error InvalidMaxConcurrentSessions => Error.Validation(
        code: "Application.InvalidMaxConcurrentSessions",
        description: "Maximum concurrent sessions must be between 1 and 100.");

    public static Error NotEnabledForOrganization => Error.NotFound(
        code: "Application.NotEnabledForOrganization",
        description: "This application is not enabled for this organization.");

    public static Error AlreadyEnabledForOrganization => Error.Conflict(
        code: "Application.AlreadyEnabledForOrganization",
        description: "This application is already enabled for this organization.");
}
