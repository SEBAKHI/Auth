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
        description: $"Organization with ID '{organizationId}' was not found.",
        metadata: new() { ["args"] = new object[] { organizationId } });

    public static Error NotFoundByCode(string code) => Error.NotFound(
        code: "Organization.NotFoundByCode",
        description: $"Organization with code '{code}' was not found.",
        metadata: new() { ["args"] = new object[] { code } });

    public static Error DuplicateCode(string code) => Error.Conflict(
        code: "Organization.DuplicateCode",
        description: $"An organization with code '{code}' already exists.",
        metadata: new() { ["args"] = new object[] { code } });

    public static Error Inactive(Guid organizationId) => Error.Forbidden(
        code: "Organization.Inactive",
        description: "This organization is currently inactive.");

    /// <summary>
    /// Self-service organization creation is shut by configuration. Returned
    /// before the duplicate-code lookup, so a closed server answers the same for a
    /// code that is taken and one that is free.
    /// </summary>
    /// <remarks>
    /// Not a permission failure, and the message avoids saying so: a platform
    /// administrator passes this check, but an ordinary user is not missing a
    /// grant that anybody could give them — the capability is off for everyone
    /// here. Telling them to ask for a permission would send them somewhere no
    /// answer lives.
    /// </remarks>
    public static Error SelfServiceCreationClosed => Error.Forbidden(
        code: "Organization.SelfServiceCreationClosed",
        description: "Organizations cannot be created on this server.");

    /// <summary>
    /// The user already owns as many self-service organizations as
    /// <c>Organizations:MaxSelfServiceOrganizationsPerUser</c> allows. Personal
    /// (auto-created) organizations do not count; platform administrators
    /// creating through the console are not limited.
    /// </summary>
    public static readonly Error SelfServiceLimitReached = Error.Forbidden(
        code: "Organization.SelfServiceLimitReached",
        description: "You already own the maximum number of organizations you can create yourself.");

    /// <summary>
    /// The application cannot create organizations right now: it is unknown or
    /// switched off, an administrator has not allowed it, its creator role is no
    /// longer usable, or it admits invited users only. One code for all of them,
    /// so a caller learns nothing about how an application is configured.
    /// </summary>
    public static readonly Error CreationFromApplicationUnavailable = Error.Forbidden(
        code: "Organization.CreationFromApplicationUnavailable",
        description: "This application cannot create an organization for you.");

    public static Error CannotDeleteWithMembers => Error.Forbidden(
        code: "Organization.CannotDeleteWithMembers",
        description: "Cannot delete an organization that still has members. Remove all members first.");

    public static Error NotOwner => Error.Forbidden(
        code: "Organization.NotOwner",
        description: "Only the organization owner can perform this action.");

    public static Error CannotTransferOwnership => Error.Forbidden(
        code: "Organization.CannotTransferOwnership",
        description: "Cannot transfer ownership to a non-member of the organization.");

    public static Error CannotTransferPersonalOrganization => Error.Forbidden(
        code: "Organization.CannotTransferPersonalOrganization",
        description: "Ownership of a personal (auto-created) organization cannot be transferred.");

    public static Error CannotTransferToSelf => Error.Validation(
        code: "Organization.CannotTransferToSelf",
        description: "This member is already the organization owner.");

    public static Error TransferTargetNotEligible => Error.Validation(
        code: "Organization.TransferTargetNotEligible",
        description: "The selected member must be an active account with a confirmed email address to receive ownership.");

    public static Error TransferCodeRequired => Error.Validation(
        code: "Organization.TransferCodeRequired",
        description: "The confirmation code sent to the new owner is required to complete the transfer.");

    public static Error InvalidOrExpiredTransferCode => Error.Forbidden(
        code: "Organization.InvalidOrExpiredTransferCode",
        description: "The confirmation code is invalid or has expired. Request a new code and try again.");

    public static Error TransferCodeTooManyAttempts => Error.Forbidden(
        code: "Organization.TransferCodeTooManyAttempts",
        description: "Too many incorrect confirmation attempts. Request a new code and try again.");

    public static Error TooManyTransferRequests => Error.Forbidden(
        code: "Organization.TooManyTransferRequests",
        description: "Too many transfer codes were requested. Please wait before trying again.");

    public static Error TransferCodeEmailFailed => Error.Failure(
        code: "Organization.TransferCodeEmailFailed",
        description: "Failed to send the confirmation code email. Please try again.");

    public static Error ConcurrentTransferConflict => Error.Conflict(
        code: "Organization.ConcurrentTransferConflict",
        description: "The organization's ownership changed while processing this transfer. Reload and try again.");

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

    public static Error CannotChangeOwnerRole => Error.Forbidden(
        code: "Organization.CannotChangeOwnerRole",
        description: "The organization owner's role cannot be changed through member management. Transfer ownership instead.");

    public static Error CannotAssignOwnerRole => Error.Forbidden(
        code: "Organization.CannotAssignOwnerRole",
        description: "The organization owner role cannot be assigned through member management. Use ownership transfer.");

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
        description: $"Application with ID '{applicationId}' was not found.",
        metadata: new() { ["args"] = new object[] { applicationId } });

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
        description: $"Role with ID '{roleId}' was not found.",
        metadata: new() { ["args"] = new object[] { roleId } });

    public static Error RoleNotForApplication(Guid roleId, Guid applicationId) => Error.Validation(
        code: "Organization.RoleNotForApplication",
        description: "The specified role does not belong to the specified application.");

    public static Error InvalidMembershipRole(Guid roleId) => Error.Validation(
        code: "Organization.InvalidMembershipRole",
        description: "An organization membership role must be an organization-level role, not an application-scoped role.",
        metadata: new() { ["args"] = new object[] { roleId } });

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
        description: $"Permission with ID '{permissionId}' was not found.",
        metadata: new() { ["args"] = new object[] { permissionId } });

    public static Error PermissionNotForApplication(Guid permissionId, Guid applicationId) => Error.Validation(
        code: "Organization.PermissionNotForApplication",
        description: "The specified permission does not belong to the specified application.");

    #endregion

    #region Invitation Errors

    public static Error InvitationNotFound(Guid invitationId) => Error.NotFound(
        code: "Organization.InvitationNotFound",
        description: $"Invitation with ID '{invitationId}' was not found.",
        metadata: new() { ["args"] = new object[] { invitationId } });

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
        description: $"A pending invitation already exists for '{email}'.",
        metadata: new() { ["args"] = new object[] { email } });

    public static Error CannotInviteSelf => Error.Validation(
        code: "Organization.CannotInviteSelf",
        description: "You cannot invite yourself to an organization.");

    public static Error InvitationEmailMismatch => Error.Forbidden(
        code: "Organization.InvitationEmailMismatch",
        description: "This invitation was sent to a different email address.");

    #endregion

    public static readonly Error InvitationCannotBeAccepted = Error.Validation(
        code: "Organization.InvitationCannotBeAccepted",
        description: "This invitation cannot be accepted.");

    public static readonly Error InvitationNotPending = Error.Validation(
        code: "Organization.InvitationNotPending",
        description: "Only a pending invitation can be changed.");

    // Request-validation rules (ADR 0001): validators declare these with
    // WithErrorCode, and the validation behavior carries the offending property.

    public static readonly Error CodeInvalidFormat = Error.Validation(
        code: "Organization.CodeInvalidFormat",
        description: "Organization code must contain only letters, numbers, hyphens, and underscores.");

    public static readonly Error CodeRequired = Error.Validation(
        code: "Organization.CodeRequired",
        description: "Organization code is required.");

    public static readonly Error CodeTooLong = Error.Validation(
        code: "Organization.CodeTooLong",
        description: "Organization code must not exceed 50 characters.");

    public static readonly Error DescriptionTooLong = Error.Validation(
        code: "Organization.DescriptionTooLong",
        description: "Description must not exceed 1000 characters.");

    public static readonly Error IdRequired = Error.Validation(
        code: "Organization.IdRequired",
        description: "Organization ID is required.");

    public static readonly Error InvitationIdRequired = Error.Validation(
        code: "Organization.InvitationIdRequired",
        description: "Invitation ID is required.");

    public static readonly Error InvitationTokenRequired = Error.Validation(
        code: "Organization.InvitationTokenRequired",
        description: "Token is required.");

    public static readonly Error LogoUrlTooLong = Error.Validation(
        code: "Organization.LogoUrlTooLong",
        description: "URL must not exceed 500 characters.");

    public static readonly Error NameRequired = Error.Validation(
        code: "Organization.NameRequired",
        description: "Organization name is required.");

    public static readonly Error NameTooLong = Error.Validation(
        code: "Organization.NameTooLong",
        description: "Organization name must not exceed 200 characters.");

    public static readonly Error NewOwnerIdRequired = Error.Validation(
        code: "Organization.NewOwnerIdRequired",
        description: "The new owner is required.");

    public static readonly Error SubscriptionTierTooLong = Error.Validation(
        code: "Organization.SubscriptionTierTooLong",
        description: "Subscription tier must not exceed 50 characters.");

    public static readonly Error TransferCodeInvalidFormat = Error.Validation(
        code: "Organization.TransferCodeInvalidFormat",
        description: "The confirmation code must be 6 digits.");

    public static readonly Error WebsiteTooLong = Error.Validation(
        code: "Organization.WebsiteTooLong",
        description: "Website URL must not exceed 500 characters.");

    // Raised by handlers when the seed data the organization roles depend on is
    // missing: a fault of the installation, reported as a value.
    public static readonly Error OwnerRoleNotFound = Error.Unexpected(
        code: "Organization.OwnerRoleNotFound",
        description: "System configuration error: Organization owner role not found.");

    public static readonly Error AdminRoleNotFound = Error.Unexpected(
        code: "Organization.AdminRoleNotFound",
        description: "System configuration error: Organization admin role not found.");
}
