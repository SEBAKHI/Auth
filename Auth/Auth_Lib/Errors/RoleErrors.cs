using ErrorOr;

namespace Auth_Lib.Errors;

/// <summary>
/// Domain errors related to role operations.
/// </summary>
public static class RoleErrors
{
    public static Error NotFound(Guid roleId) => Error.NotFound(
        code: "Role.NotFound",
        description: $"Role with ID '{roleId}' was not found.");

    public static Error NotFoundByCode(string code) => Error.NotFound(
        code: "Role.NotFoundByCode",
        description: $"Role with code '{code}' was not found.");

    public static Error DuplicateCode(string code, Guid applicationId) => Error.Conflict(
        code: "Role.DuplicateCode",
        description: $"A role with code '{code}' already exists in this application.");

    public static Error CannotDeleteSystemRole => Error.Forbidden(
        code: "Role.CannotDeleteSystemRole",
        description: "System roles cannot be deleted.");

    public static Error CannotModifySystemRole => Error.Forbidden(
        code: "Role.CannotModifySystemRole",
        description: "System roles cannot be modified.");

    public static Error RoleInactive => Error.Forbidden(
        code: "Role.Inactive",
        description: "This role is currently inactive.");

    public static Error RoleAlreadyAssigned(Guid userId, Guid roleId) => Error.Conflict(
        code: "Role.AlreadyAssigned",
        description: "This role is already assigned to the user.");

    public static Error RoleNotAssigned(Guid userId, Guid roleId) => Error.NotFound(
        code: "Role.NotAssigned",
        description: "This role is not assigned to the user.");

    public static Error CannotRemoveLastAdminRole => Error.Forbidden(
        code: "Role.CannotRemoveLastAdmin",
        description: "Cannot remove the last admin role. At least one admin must remain.");
}
