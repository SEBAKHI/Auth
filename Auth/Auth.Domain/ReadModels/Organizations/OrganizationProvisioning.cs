using Auth.Domain.Entities;

namespace Auth.Domain.ReadModels.Organizations;

/// <summary>
/// An organization the user owns that could be set up for an application
/// instead of creating a new one: active, not a personal (auto-created)
/// organization, the owner still an active member, and not set up yet.
/// </summary>
public sealed record OrganizationSetupCandidate(Guid Id, string Name);

/// <summary>
/// One organization-creation step for an application: either a new
/// organization (<see cref="NewOrganization"/>) or one the user already owns
/// (<see cref="ExistingOrganizationId"/>), never both.
/// </summary>
/// <param name="UserId">The signed-in user: the owner, and the one granted the role.</param>
/// <param name="ApplicationId">The application to enable for the organization.</param>
/// <param name="CreatorRoleId">The application's creator role, checked by the caller.</param>
/// <param name="OwnerRoleId">The organization owner role bound to a new organization's membership.</param>
/// <param name="MaxSelfServiceOrganizations">How many self-service organizations the user may own.</param>
public sealed record OrganizationProvisioningRequest(
    Guid UserId,
    Guid ApplicationId,
    Guid CreatorRoleId,
    Guid OwnerRoleId,
    int MaxSelfServiceOrganizations,
    Organization? NewOrganization,
    Guid? ExistingOrganizationId)
{
    /// <summary>A step that creates <paramref name="organization"/>.</summary>
    public static OrganizationProvisioningRequest ForNewOrganization(
        Organization organization,
        Guid applicationId,
        Guid creatorRoleId,
        Guid ownerRoleId,
        int maxSelfServiceOrganizations) =>
        new(organization.OwnerId, applicationId, creatorRoleId, ownerRoleId,
            maxSelfServiceOrganizations, organization, null);

    /// <summary>A step that sets up an organization the user already owns.</summary>
    public static OrganizationProvisioningRequest ForExistingOrganization(
        Guid organizationId,
        Guid userId,
        Guid applicationId,
        Guid creatorRoleId) =>
        new(userId, applicationId, creatorRoleId, Guid.Empty, 0, null, organizationId);
}

/// <summary>How an organization-creation step ended.</summary>
public enum OrganizationProvisioningStatus
{
    /// <summary>Committed: the organization is set up for the application.</summary>
    Provisioned,

    /// <summary>The user already had an organization set up for the application; nothing was written.</summary>
    AlreadySetUp,

    /// <summary>The user owns as many self-service organizations as allowed; nothing was written.</summary>
    LimitReached,

    /// <summary>The existing organization is not the user's, or not eligible; nothing was written.</summary>
    OrganizationNotEligible,

    /// <summary>The generated organization code was taken; nothing was written, retry with another.</summary>
    CodeTaken,
}

/// <summary>The result of an organization-creation step.</summary>
/// <param name="OrganizationId">The organization set up, when the status is Provisioned or AlreadySetUp.</param>
/// <param name="OrganizationCreated">True only when this step created the organization.</param>
public sealed record OrganizationProvisioningOutcome(
    OrganizationProvisioningStatus Status,
    Guid? OrganizationId = null,
    bool OrganizationCreated = false);
