namespace Auth.Domain.ReadModels.Organizations;

/// <summary>
/// What removing a member took with it: the application roles and the permissions the
/// person held in that organization, deleted in the same transaction as the membership.
/// </summary>
/// <param name="MembershipRemoved">
/// Whether the membership row was deleted. False when the person was no longer a member,
/// or had become the organization's owner, by the time the statement ran; nothing is
/// deleted then.
/// </param>
public sealed record OrganizationMemberRemoval(
    bool MembershipRemoved,
    IReadOnlyList<RemovedOrganizationApplicationRole> ApplicationRoles,
    IReadOnlyList<RemovedOrganizationPermission> Permissions);

/// <summary>An application role a removed member held in the organization.</summary>
public sealed record RemovedOrganizationApplicationRole(
    Guid ApplicationId,
    Guid RoleId,
    string? RoleCode,
    string? RoleName);

/// <summary>A permission granted to a removed member in the organization.</summary>
public sealed record RemovedOrganizationPermission(
    Guid ApplicationId,
    Guid PermissionId,
    string? PermissionCode);
