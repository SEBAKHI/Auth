using Auth.Domain.Enums;

namespace Auth.Domain.ReadModels.Access;

/// <summary>
/// One application a user can sign in to, with how: the application is open to
/// everyone, and/or the user holds an invitation on its access list.
/// </summary>
/// <remarks>
/// Mirrors the sign-in gate exactly. An open application admits everyone, so it
/// is listed for every user with <see cref="ViaOpenAccess"/> set; a restricted
/// one is listed only for the people on its list.
/// </remarks>
public sealed record UserApplicationAccess(
    Guid ApplicationId,
    string Code,
    string Name,
    string? LogoUrl,
    bool IsActive,
    bool ViaOpenAccess,
    bool ViaGrant);

/// <summary>
/// One user attached to an application: invited on its access list, holding an
/// app-scoped role directly (UserRoles), and/or holding one through an
/// organization (OrganizationUserRoles).
/// </summary>
/// <remarks>
/// Uses init-only properties (not a positional constructor) so Dapper can
/// materialize the <see cref="UserStatus"/> enum from the tinyint column.
/// <para>
/// Attachment is not the same question as admission: a user with only an
/// app-scoped role is listed here but cannot sign in to a restricted
/// application, and everyone can sign in to an open one without appearing here.
/// </para>
/// </remarks>
public sealed record ApplicationUserRow
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public UserStatus Status { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? RoleNames { get; init; }
    public bool ViaGrant { get; init; }
    public bool ViaDirect { get; init; }
    public bool ViaOrganization { get; init; }
}

/// <summary>
/// One invitation on an application's access list, with the invited user's
/// display details and who issued it.
/// </summary>
/// <remarks>
/// Uses init-only properties (not a positional constructor) so Dapper can
/// materialize the <see cref="UserStatus"/> enum from the tinyint column.
/// </remarks>
public sealed record ApplicationUserGrantRow
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public UserStatus Status { get; init; }
    public DateTime GrantedAt { get; init; }
    public Guid GrantedBy { get; init; }
    public string? GrantedByName { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// One application an organization is allowed to enable: switched on, open to
/// everyone, and not already enabled for that organization.
/// </summary>
public sealed record AvailableApplicationRow(
    Guid ApplicationId,
    string Code,
    string Name,
    string? LogoUrl);

/// <summary>
/// One organization that has an application enabled (active or not),
/// with the enablement link details.
/// </summary>
public sealed record ApplicationOrganizationRow(
    Guid OrganizationId,
    string Code,
    string Name,
    string? LogoUrl,
    bool OrganizationIsActive,
    bool LinkIsActive,
    DateTime EnabledAt,
    DateTime? ExpiresAt,
    int MemberCount);

/// <summary>
/// One user holding an active assignment of a specific role, directly
/// (UserRoles) and/or through an organization (OrganizationUserRoles).
/// </summary>
/// <remarks>
/// Uses init-only properties (not a positional constructor) so Dapper can
/// materialize the <see cref="UserStatus"/> enum from the tinyint column.
/// </remarks>
public sealed record RoleUserRow
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public UserStatus Status { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool ViaDirect { get; init; }
    public bool ViaOrganization { get; init; }
    public string? OrganizationNames { get; init; }
}

/// <summary>
/// One application related to a role: the role's owning application and/or
/// an application appearing on active assignments of the role.
/// </summary>
public sealed record RoleApplicationRow(
    Guid ApplicationId,
    string Code,
    string Name,
    string? LogoUrl,
    bool IsActive,
    bool IsOwner,
    bool IsAssigned);

/// <summary>
/// One user granted a specific permission via a direct grant (UserPermissions),
/// an organization grant (OrganizationUserPermissions), and/or a role that
/// contains the permission (RolePermissions through active role assignments).
/// </summary>
/// <remarks>
/// Uses init-only properties (not a positional constructor) so Dapper can
/// materialize the <see cref="UserStatus"/> enum from the tinyint column.
/// </remarks>
public sealed record PermissionUserRow
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? DisplayName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public UserStatus Status { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool ViaDirect { get; init; }
    public bool ViaOrganization { get; init; }
    public bool ViaRole { get; init; }
    public string? RoleNames { get; init; }
}
