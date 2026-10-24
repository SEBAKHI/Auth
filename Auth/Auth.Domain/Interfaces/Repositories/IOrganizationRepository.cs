using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

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
    /// Same as <see cref="GetMembershipPermissionCodesAsync(Guid, CancellationToken)"/>,
    /// narrowed to organizations that actually have the given application
    /// enabled. Used when minting an application-scoped token, so a partner
    /// application is not handed the user's entire organization graph.
    /// </summary>
    /// <remarks>
    /// Distinct name rather than an overload: an overload taking two
    /// <see cref="Guid"/>s would be ambiguous with the
    /// (organizationId, userId) one below at every call site.
    /// </remarks>
    Task<IReadOnlyList<(Guid OrganizationId, string Code)>> GetMembershipPermissionCodesForApplicationAsync(
        Guid userId,
        Guid applicationId,
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
    /// One page of an organization's members as an application reading its directory sees them
    /// (OI-194): active memberships not expired at <paramref name="now"/>, of accounts that are
    /// not deleted, oldest first (joined at, then membership id), with the total those rules
    /// match. Stricter than <see cref="GetMembersPagedAsync"/> on purpose: an application must
    /// never receive a deleted account or a membership that has lapsed.
    /// </summary>
    Task<(IReadOnlyList<ReadModels.Organizations.ApplicationDirectoryMemberRow> Members, int TotalCount)> GetActiveMembersPagedAsync(
        Guid organizationId,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="GetActiveMembersPagedAsync"/>, narrowed to the members who hold at least one
    /// role of <paramref name="applicationId"/> in the organization by the rule of
    /// <see cref="GetMemberApplicationRolesAsync"/> (OI-202, D-202-3): what an application reads
    /// with no user behind it. The total counts the same narrowed set.
    /// </summary>
    Task<(IReadOnlyList<ReadModels.Organizations.ApplicationDirectoryMemberRow> Members, int TotalCount)> GetActiveMembersHoldingApplicationRolePagedAsync(
        Guid organizationId,
        Guid applicationId,
        DateTime now,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// The roles of one application that each of <paramref name="userIds"/> holds in an
    /// organization: active assignments not expired at <paramref name="now"/>, of active roles
    /// that belong to <paramref name="applicationId"/>. One query for a whole page of members; never
    /// another application's roles.
    /// </summary>
    Task<IReadOnlyList<ReadModels.Organizations.MemberApplicationRoleRow>> GetMemberApplicationRolesAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyCollection<Guid> userIds,
        DateTime now,
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
    /// Every application role the user holds through any organization and that the
    /// user's application tokens carry (active, unexpired, an active role), with the
    /// organization, the application (deleted ones included) and the role, in one read.
    /// </summary>
    Task<IReadOnlyList<ReadModels.Access.UserOrganizationRoleRow>> GetUserApplicationRolesAcrossOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Assigns an app-level role to a user within an organization: writes the
    /// assignment on its unique key, re-activating an expired or inactive row in place
    /// with the new terms, or inserting one. Returns the id of the row that holds it.
    /// </summary>
    Task<Guid> AssignAppRoleAsync(
        OrganizationUserRole assignment,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an app-level role from a user within an organization, in whatever state
    /// the row is (expired or inactive included). Returns whether a row was removed.
    /// </summary>
    Task<bool> RemoveAppRoleAsync(
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
    /// Gets the user's effective permission codes for one application across
    /// every organization they belong to that has it enabled — the single-query
    /// form of "walk the memberships, then ask each one". Used on the token-mint
    /// path, where the per-membership loop cost 1 + 2N round trips.
    /// </summary>
    /// <remarks>
    /// Returns codes with no organization attached, so it answers "does this user
    /// hold X anywhere inside this application" and nothing narrower. That is the
    /// right question for a server-side capability check and the WRONG one for a
    /// token: see <see cref="GetEffectivePermissionPairsForApplicationAsync"/>.
    /// </remarks>
    Task<IReadOnlyList<string>> GetEffectivePermissionCodesForApplicationAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same effective permissions, each paired with the organization that
    /// granted it.
    /// </summary>
    /// <remarks>
    /// This is what a token must carry. A user who belongs to two organizations
    /// that both enable an application receives one token; flattening their
    /// delegated permissions into a single unscoped list means a relying party
    /// cannot tell which organization a permission came from, and the shipped SDK
    /// - authorizing on exactly that flat list - would grant a permission held in
    /// one organization while the caller acts on another's data. The pairing is
    /// the only thing that makes the distinction recoverable.
    /// </remarks>
    Task<IReadOnlyList<(Guid OrganizationId, string Code)>> GetEffectivePermissionPairsForApplicationAsync(
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
    /// Gets an invitation by any fingerprint its token may be stored under.
    /// </summary>
    /// <remarks>
    /// Takes the fingerprints, never the token. Callers derive them from what the
    /// caller presented and look the row up by them, so the plaintext invitation token exists only in
    /// the email and in the inbound request — never in the database, and never in
    /// a query this repository issues.
    /// </remarks>
    Task<OrganizationInvitation?> GetInvitationByTokenHashAsync(
        CredentialHashCandidates candidates,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets every invitation of an organization STORED as pending, expired or not. This is the
    /// duplicate check's rule when inviting (an address with such a row cannot be invited
    /// again); the list the organization reads is <see cref="GetInvitationsPagedAsync"/>.
    /// </summary>
    Task<IReadOnlyList<OrganizationInvitation>> GetPendingInvitationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// One page of an organization's invitations, narrowed by <paramref name="filter"/> (its
    /// statuses, and its application when it names one), with the total the filter matches. Effective statuses are decided against
    /// <paramref name="now"/> with the rule of <see cref="OrganizationInvitation.EffectiveStatus"/>.
    /// <paramref name="sortBy"/> accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.OrganizationInvitations"/>; null keeps the default order,
    /// newest first.
    /// </summary>
    Task<(IReadOnlyList<OrganizationInvitation> Invitations, int TotalCount)> GetInvitationsPagedAsync(
        Guid organizationId,
        ReadModels.Organizations.InvitationListFilter filter,
        DateTime now,
        int pageNumber,
        int pageSize,
        string? sortBy,
        SortDirection sortDirection,
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
    /// Records an accepted invitation in one transaction: flips it from Pending to its
    /// accepted state, adds the membership when there is one to add, and adds the
    /// application role when there is one to add. Returns false, having written
    /// nothing, when the invitation is no longer pending (a concurrent acceptance).
    /// </summary>
    Task<bool> AcceptInvitationAsync(
        OrganizationInvitation invitation,
        OrganizationUser? membership,
        OrganizationUserRole? applicationRole,
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

    #region Organization creation from an application

    /// <summary>
    /// Counts the organizations a user owns that count toward the self-service
    /// limit: every owned organization except personal (auto-created) ones.
    /// </summary>
    Task<int> CountSelfServiceOwnedAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the organization the user owns that is set up for the application:
    /// active, the owner an active member, the application enabled there, and
    /// the creator role held there. The same definition the provisioning
    /// transaction asserts before it commits, so the authorize endpoint and the
    /// creation step cannot disagree and loop.
    /// </summary>
    /// <returns>The organization's id, or null when none is set up.</returns>
    Task<Guid?> FindOrganizationSetUpForApplicationAsync(
        Guid userId,
        Guid applicationId,
        Guid creatorRoleId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The organizations the user owns that could be set up for the application
    /// instead of creating one, ordered by name.
    /// </summary>
    Task<IReadOnlyList<ReadModels.Organizations.OrganizationSetupCandidate>> GetOrganizationSetupCandidatesAsync(
        Guid userId,
        Guid applicationId,
        Guid creatorRoleId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets up an organization for an application in ONE transaction: creates
    /// it (with the owner membership) or takes one the user owns, enables the
    /// application for it, and grants the creator role there. Counts the user's
    /// self-service organizations under a lock first, so concurrent requests
    /// cannot pass the limit together; an organization already set up is
    /// returned unchanged.
    /// </summary>
    Task<ReadModels.Organizations.OrganizationProvisioningOutcome> ProvisionForApplicationAsync(
        ReadModels.Organizations.OrganizationProvisioningRequest request,
        CancellationToken cancellationToken);

    #endregion
}
