using Auth.Domain.Entities;
using Auth.Domain.Enums;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for organization operations.
/// Handles organizations, memberships, app subscriptions, and permissions.
/// </summary>
public interface IOrganizationRepository
{
    #region Organization CRUD

    /// <summary>
    /// Gets an organization by its ID.
    /// </summary>
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets an organization by its code.
    /// </summary>
    Task<Organization?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an organization code exists.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all organizations owned by a user.
    /// </summary>
    Task<IReadOnlyList<Organization>> GetByOwnerAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new organization.
    /// </summary>
    Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing organization.
    /// </summary>
    Task UpdateAsync(Organization organization, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an organization and all related data.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a paginated list of ALL organizations (platform administration),
    /// with optional search over name/code/contact email.
    /// </summary>
    Task<(IReadOnlyList<Organization> Organizations, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically transfers organization ownership: sets the new OwnerId,
    /// promotes the new owner's membership to the owner role, and demotes the
    /// previous owner's membership to the given role — all in one transaction.
    /// Fails (returns false, no changes) when the organization's owner is no
    /// longer <paramref name="previousOwnerId"/> (concurrent transfer) or the
    /// new owner has no membership row.
    /// </summary>
    Task<bool> TransferOwnershipAsync(
        Guid organizationId,
        Guid previousOwnerId,
        Guid newOwnerId,
        Guid ownerRoleId,
        Guid demotedRoleId,
        Guid modifiedBy,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets active member counts for a set of organizations in one query.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetMemberCountsAsync(
        IReadOnlyCollection<Guid> organizationIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets enabled application counts for a set of organizations in one query.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetEnabledApplicationCountsAsync(
        IReadOnlyCollection<Guid> organizationIds,
        CancellationToken cancellationToken);

    #endregion

    #region Organization Membership

    /// <summary>
    /// Gets a user's membership in an organization.
    /// </summary>
    Task<OrganizationUser?> GetMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all organizations a user is a member of.
    /// </summary>
    Task<IReadOnlyList<Organization>> GetUserOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active organization memberships for a user.
    /// </summary>
    Task<IReadOnlyList<OrganizationUser>> GetUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the permission codes granted by the user's membership role in each
    /// organization they belong to. Feeds the token's org-scoped claims.
    /// </summary>
    Task<IReadOnlyList<(Guid OrganizationId, string Code)>> GetMembershipPermissionCodesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the permission codes granted by the user's membership role in one
    /// organization (empty when not a member). Used by the authorization gate
    /// as a live fallback for tokens issued before the membership existed.
    /// </summary>
    Task<IReadOnlyList<string>> GetMembershipPermissionCodesAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all members of an organization.
    /// </summary>
    Task<IReadOnlyList<OrganizationUser>> GetMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets members of an organization with pagination. <paramref name="sortBy"/>
    /// accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.OrganizationMembers"/>; null keeps the default order.
    /// </summary>
    Task<(IReadOnlyList<OrganizationUser> Members, int TotalCount)> GetMembersPagedAsync(
        Guid organizationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a user to an organization.
    /// </summary>
    Task<OrganizationUser> AddMemberAsync(
        OrganizationUser membership,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates a member's organization role.
    /// </summary>
    Task UpdateMemberAsync(
        OrganizationUser membership,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a user from an organization.
    /// </summary>
    Task RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user is a member of an organization.
    /// </summary>
    Task<bool> IsMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    #endregion

    #region Organization Applications (Subscriptions)

    /// <summary>
    /// Gets all applications enabled for an organization.
    /// </summary>
    Task<IReadOnlyList<OrganizationApplication>> GetEnabledApplicationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a specific application subscription for an organization.
    /// </summary>
    Task<OrganizationApplication?> GetApplicationSubscriptionAsync(
        Guid organizationId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enables an application for an organization.
    /// </summary>
    Task<OrganizationApplication> EnableApplicationAsync(
        OrganizationApplication subscription,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates an application subscription.
    /// </summary>
    Task UpdateApplicationSubscriptionAsync(
        OrganizationApplication subscription,
        CancellationToken cancellationToken);

    /// <summary>
    /// Disables an application for an organization.
    /// </summary>
    Task DisableApplicationAsync(
        Guid organizationId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an application is enabled for an organization.
    /// </summary>
    Task<bool> IsApplicationEnabledAsync(
        Guid organizationId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets, per application, the number of distinct users with an active,
    /// unexpired app-role assignment or direct permission grant within an
    /// organization. Applications with no assigned users are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetAssignedUserCountsAsync(
        Guid organizationId,
        CancellationToken cancellationToken);

    #endregion

    #region Organization User Roles (App-level roles within org)

    /// <summary>
    /// Gets all app-level role assignments for a user within an organization.
    /// </summary>
    Task<IReadOnlyList<OrganizationUserRole>> GetUserAppRolesAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all app-level role assignments for a user within an organization for a specific app.
    /// </summary>
    Task<IReadOnlyList<OrganizationUserRole>> GetUserAppRolesAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Assigns an app-level role to a user within an organization.
    /// </summary>
    Task<OrganizationUserRole> AssignAppRoleAsync(
        OrganizationUserRole assignment,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an app-level role from a user within an organization.
    /// </summary>
    Task RemoveAppRoleAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid roleId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific app-level role within an organization.
    /// </summary>
    Task<bool> HasAppRoleAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid roleId,
        CancellationToken cancellationToken);

    #endregion

    #region Organization User Permissions (Individual grants within org)

    /// <summary>
    /// Gets all individual permission grants for a user within an organization.
    /// </summary>
    Task<IReadOnlyList<OrganizationUserPermission>> GetUserPermissionsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all individual permission grants for a user within an organization for a specific app.
    /// </summary>
    Task<IReadOnlyList<OrganizationUserPermission>> GetUserPermissionsAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Grants a permission to a user within an organization.
    /// </summary>
    Task<OrganizationUserPermission> GrantPermissionAsync(
        OrganizationUserPermission grant,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a permission from a user within an organization.
    /// </summary>
    Task RevokePermissionAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid permissionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific permission within an organization for an app.
    /// </summary>
    Task<bool> HasPermissionAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid permissionId,
        CancellationToken cancellationToken);

    #endregion

    #region Authorization Helpers

    /// <summary>
    /// Gets all effective permission codes for a user within an organization for a specific app.
    /// This includes permissions from both app-level roles and individual grants.
    /// </summary>
    Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has access to an application through any of their organizations.
    /// Returns true if:
    /// 1. User is a member of at least one org that has the app enabled
    /// 2. User has at least one role OR permission for that app in that org
    /// </summary>
    Task<bool> HasAppAccessAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific permission for an app through any of their organizations.
    /// Returns true if any org grants the permission (via role or direct grant).
    /// </summary>
    Task<bool> HasPermissionInAnyOrgAsync(
        Guid userId,
        Guid applicationId,
        string permissionCode,
        CancellationToken cancellationToken);

    #endregion

    #region Invitations

    /// <summary>
    /// Gets an invitation by its ID.
    /// </summary>
    Task<OrganizationInvitation?> GetInvitationByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets an invitation by its token.
    /// </summary>
    Task<OrganizationInvitation?> GetInvitationByTokenAsync(
        string token,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all pending invitations for an organization.
    /// </summary>
    Task<IReadOnlyList<OrganizationInvitation>> GetPendingInvitationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all pending invitations for an email address.
    /// </summary>
    Task<IReadOnlyList<OrganizationInvitation>> GetPendingInvitationsByEmailAsync(
        string email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new invitation.
    /// </summary>
    Task<OrganizationInvitation> CreateInvitationAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates an invitation.
    /// </summary>
    Task UpdateInvitationAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an invitation.
    /// </summary>
    Task DeleteInvitationAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks expired invitations as expired.
    /// </summary>
    Task MarkExpiredInvitationsAsync(CancellationToken cancellationToken);

    #endregion
}
