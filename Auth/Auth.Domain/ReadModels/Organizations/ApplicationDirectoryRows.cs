namespace Auth.Domain.ReadModels.Organizations;

/// <summary>
/// One member of an organization as an application reading its directory sees them (OI-194):
/// an active, unexpired membership of an account that is not deleted, with the user's own
/// columns and the membership role's code read in the same row.
/// </summary>
/// <param name="MembershipId">The membership row's id; the order's tie-breaker.</param>
/// <param name="ProfileImageUrl">The stored image key or URL, not yet composed.</param>
public sealed record ApplicationDirectoryMemberRow(
    Guid MembershipId,
    Guid UserId,
    string? FirstName,
    string? LastName,
    string Email,
    string? ProfileImageUrl,
    string MembershipRoleCode,
    DateTime JoinedAt);

/// <summary>
/// One application role a member holds in an organization: active, unexpired, and a role of
/// the application asked about.
/// </summary>
public sealed record MemberApplicationRoleRow(Guid UserId, string RoleCode);
