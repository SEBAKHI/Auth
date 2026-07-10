using Auth.Domain.Enums;

namespace Auth.Domain.ReadModels.Access;

/// <summary>
/// One application a user can access, with how the access is obtained:
/// through an organization (membership + enabled app + app-level role or
/// permission in that organization) and/or a direct app-scoped role assignment.
/// </summary>
public sealed record UserApplicationAccess(
    Guid ApplicationId,
    string Code,
    string Name,
    string? LogoUrl,
    bool IsActive,
    bool ViaOrganization,
    bool ViaDirect);

/// <summary>
/// One user holding an active role assignment scoped to an application,
/// either directly (UserRoles) or through an organization (OrganizationUserRoles).
/// </summary>
/// <remarks>
/// Uses init-only properties (not a positional constructor) so Dapper can
/// materialize the <see cref="UserStatus"/> enum from the tinyint column.
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
}

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
