namespace Auth.Domain.Constants;

/// <summary>
/// Codes of the built-in organization-level (application-agnostic) membership
/// roles. These are the roles a member can hold within an organization.
/// </summary>
public static class OrganizationRoleCodes
{
    /// <summary>Full control over the organization (org:*). Held by exactly the owner.</summary>
    public const string Owner = "org-owner";

    /// <summary>Manages members and app subscriptions, below the owner.</summary>
    public const string Admin = "org-admin";

    /// <summary>Basic membership; access apps per granted permissions.</summary>
    public const string Member = "org-member";

    /// <summary>
    /// The membership roles an inviter or a member manager may hand out. An allowlist,
    /// not "every role without an application minus the owner": platform roles have no
    /// application either, so the subtraction offered super-admin in the picker and
    /// accepted it on save. The owner is absent on purpose; ownership moves only through
    /// the owner-only transfer.
    /// </summary>
    public static readonly IReadOnlyList<string> AssignableMembershipRoles = [Admin, Member];
}
