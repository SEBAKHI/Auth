using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to organization operations.
/// </summary>
public static class OrganizationErrors
{
    #region Organization Errors

    public static Error NotFound(Guid organizationId) => Error.NotFound(
        code: "Organization.NotFound",
        description: $"Organization with ID '{organizationId}' was not found.");

    public static Error NotFoundByCode(string code) => Error.NotFound(
        code: "Organization.NotFoundByCode",
        description: $"Organization with code '{code}' was not found.");

    public static Error DuplicateCode(string code) => Error.Conflict(
        code: "Organization.DuplicateCode",
        description: $"An organization with code '{code}' already exists.");

    public static Error Inactive(Guid organizationId) => Error.Forbidden(
        code: "Organization.Inactive",
        description: "This organization is currently inactive.");

    public static Error CannotDeleteWithMembers => Error.Forbidden(
        code: "Organization.CannotDeleteWithMembers",
        description: "Cannot delete an organization that still has members. Remove all members first.");

    public static Error NotOwner => Error.Forbidden(
        code: "Organization.NotOwner",
        description: "Only the organization owner can perform this action.");

    public static Error CannotTransferOwnership => Error.Forbidden(
        code: "Organization.CannotTransferOwnership",
        description: "Cannot transfer ownership to a non-member of the organization.");

    #endregion

    #region Membership Errors

    public static Error AlreadyMember(Guid userId, Guid organizationId) => Error.Conflict(
        code: "Organization.AlreadyMember",
        description: "User is already a member of this organization.");

    public static Error NotMember(Guid userId, Guid organizationId) => Error.NotFound(
        code: "Organization.NotMember",
        description: "User is not a member of this organization.");

    public static Error NotAMember => Error.Forbidden(
        code: "Organization.NotAMember",
        description: "You are not a member of this organization.");

    public static Error CannotRemoveOwner => Error.Forbidden(
        code: "Organization.CannotRemoveOwner",
        description: "The organization owner cannot be removed. Transfer ownership first.");

    public static Error CannotChangeOwnRole => Error.Forbidden(
        code: "Organization.CannotChangeOwnRole",
        description: "You cannot change your own organization role.");

    public static Error MembershipExpired => Error.Forbidden(
        code: "Organization.MembershipExpired",
        description: "Your membership in this organization has expired.");

    public static Error InsufficientPermissions => Error.Forbidden(
        code: "Organization.InsufficientPermissions",
        description: "You do not have sufficient permissions to perform this action.");

    #endregion

    #region Application Subscription Errors

    public static Error ApplicationNotFound(Guid applicationId) => Error.NotFound(
        code: "Organization.ApplicationNotFound",
        description: $"Application with ID '{applicationId}' was not found.");

    public static Error ApplicationAlreadyEnabled(Guid applicationId) => Error.Conflict(
        code: "Organization.ApplicationAlreadyEnabled",
        description: "This application is already enabled for the organization.");

    public static Error ApplicationNotEnabled(Guid applicationId) => Error.NotFound(
        code: "Organization.ApplicationNotEnabled",
        description: "This application is not enabled for the organization.");

    public static Error SubscriptionExpired(Guid applicationId) => Error.Forbidden(
        code: "Organization.SubscriptionExpired",
        description: "The subscription for this application has expired.");

    #endregion

    #region Role Assignment Errors

    public static Error AppRoleAlreadyAssigned(Guid userId, Guid applicationId, Guid roleId) => Error.Conflict(
        code: "Organization.AppRoleAlreadyAssigned",
        description: "This role is already assigned to the user for this application.");

    public static Error AppRoleNotAssigned(Guid userId, Guid applicationId, Guid roleId) => Error.NotFound(
        code: "Organization.AppRoleNotAssigned",
        description: "This role is not assigned to the user for this application.");

    public static Error RoleNotFound(Guid roleId) => Error.NotFound(
        code: "Organization.RoleNotFound",
        description: $"Role with ID '{roleId}' was not found.");

    public static Error RoleNotForApplication(Guid roleId, Guid applicationId) => Error.Validation(
        code: "Organization.RoleNotForApplication",
        description: "The specified role does not belong to the specified application.");

    #endregion

    #region Permission Grant Errors

    public static Error PermissionAlreadyGranted(Guid userId, Guid applicationId, Guid permissionId) => Error.Conflict(
        code: "Organization.PermissionAlreadyGranted",
        description: "This permission is already granted to the user for this application.");

    public static Error PermissionNotGranted(Guid userId, Guid applicationId, Guid permissionId) => Error.NotFound(
        code: "Organization.PermissionNotGranted",
        description: "This permission is not granted to the user for this application.");

    public static Error PermissionNotFound(Guid permissionId) => Error.NotFound(
        code: "Organization.PermissionNotFound",
        description: $"Permission with ID '{permissionId}' was not found.");

    public static Error PermissionNotForApplication(Guid permissionId, Guid applicationId) => Error.Validation(
        code: "Organization.PermissionNotForApplication",
        description: "The specified permission does not belong to the specified application.");

    #endregion

    #region Invitation Errors

    public static Error InvitationNotFound(Guid invitationId) => Error.NotFound(
        code: "Organization.InvitationNotFound",
        description: $"Invitation with ID '{invitationId}' was not found.");

    public static Error InvitationNotFoundByToken => Error.NotFound(
        code: "Organization.InvitationNotFoundByToken",
        description: "Invalid or expired invitation token.");

    public static Error InvitationExpired => Error.Forbidden(
        code: "Organization.InvitationExpired",
        description: "This invitation has expired.");

    public static Error InvitationAlreadyAccepted => Error.Conflict(
        code: "Organization.InvitationAlreadyAccepted",
        description: "This invitation has already been accepted.");

    public static Error InvitationAlreadyDeclined => Error.Conflict(
        code: "Organization.InvitationAlreadyDeclined",
        description: "This invitation has already been declined.");

    public static Error InvitationAlreadyCancelled => Error.Conflict(
        code: "Organization.InvitationAlreadyCancelled",
        description: "This invitation has been cancelled.");

    public static Error PendingInvitationExists(string email) => Error.Conflict(
        code: "Organization.PendingInvitationExists",
        description: $"A pending invitation already exists for '{email}'.");

    public static Error CannotInviteSelf => Error.Validation(
        code: "Organization.CannotInviteSelf",
        description: "You cannot invite yourself to an organization.");

    public static Error InvitationEmailMismatch => Error.Forbidden(
        code: "Organization.InvitationEmailMismatch",
        description: "This invitation was sent to a different email address.");

    #endregion
}
