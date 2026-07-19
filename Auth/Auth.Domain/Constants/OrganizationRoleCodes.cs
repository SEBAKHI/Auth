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
}
