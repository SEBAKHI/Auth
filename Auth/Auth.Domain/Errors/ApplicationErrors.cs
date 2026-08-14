using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to application operations.
/// </summary>
public static class ApplicationErrors
{
    public static Error NotFound(Guid applicationId) => Error.NotFound(
        code: "Application.NotFound",
        description: $"Application with ID '{applicationId}' was not found.",
        metadata: new() { ["args"] = new object[] { applicationId } });

    public static Error NotFoundByCode(string code) => Error.NotFound(
        code: "Application.NotFoundByCode",
        description: $"Application with code '{code}' was not found.",
        metadata: new() { ["args"] = new object[] { code } });

    public static Error DuplicateCode(string code) => Error.Conflict(
        code: "Application.DuplicateCode",
        description: $"An application with code '{code}' already exists.",
        metadata: new() { ["args"] = new object[] { code } });

    public static Error HasActiveUsers => Error.Conflict(
        code: "Application.HasActiveUsers",
        description: "Cannot delete application with active user assignments.");

    public static Error HasActiveOrganizations => Error.Conflict(
        code: "Application.HasActiveOrganizations",
        description: "Cannot delete application that is enabled for organizations.");

    public static Error ApplicationInactive => Error.Forbidden(
        code: "Application.Inactive",
        description: "This application is currently inactive.");

    /// <summary>
    /// The sign-in gate's refusal. Deliberately generic: it names neither the
    /// user, nor the application, nor why access was refused, so it cannot be
    /// used to probe who is entitled to what. The specifics go to the server log.
    /// </summary>
    public static Error AccessDenied => Error.Forbidden(
        code: "Application.AccessDenied",
        description: "You do not have access to this application.");

    public static Error UserAccessAlreadyGranted(Guid userId) => Error.Conflict(
        code: "Application.UserAccessAlreadyGranted",
        description: $"User '{userId}' already has access to this application.",
        metadata: new() { ["args"] = new object[] { userId } });

    public static Error UserAccessNotFound(Guid userId) => Error.NotFound(
        code: "Application.UserAccessNotFound",
        description: $"User '{userId}' does not have granted access to this application.",
        metadata: new() { ["args"] = new object[] { userId } });

    /// <summary>
    /// A restricted application admits only the users on its invitation list, so
    /// it can never have enabled organizations. Guards the rule from the
    /// enablement side; <see cref="HasActiveOrganizations"/> guards it from the
    /// access-mode side.
    /// </summary>
    public static Error RestrictedCannotBeEnabledForOrganization => Error.Validation(
        code: "Application.RestrictedCannotBeEnabledForOrganization",
        description: "This application is restricted to individually invited users and cannot be enabled for an organization.");

    /// <summary>
    /// The same invariant seen from the access-mode side. Distinct from
    /// <see cref="HasActiveOrganizations"/> because that one says "cannot
    /// delete", and an administrator reading it while changing who may sign in
    /// would have no idea what they were being told.
    /// </summary>
    public static Error CannotRestrictWithActiveOrganizations => Error.Conflict(
        code: "Application.CannotRestrictWithActiveOrganizations",
        description: "Disable this application for its organizations before restricting it to individually invited users.");

    public static Error InvalidCode(string code) => Error.Validation(
        code: "Application.InvalidCode",
        description: $"Application code '{code}' is invalid. Use uppercase alphanumeric characters and underscores only.",
        metadata: new() { ["args"] = new object[] { code } });

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
