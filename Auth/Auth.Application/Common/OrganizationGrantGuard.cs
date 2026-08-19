using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Common;

/// <summary>
/// The organization-scoped half of the rule <see cref="PermissionGrantGuard"/>
/// enforces on the platform: <b>no principal may grant a permission it does not
/// itself hold.</b>
/// </summary>
/// <remarks>
/// <para>
/// Two organization paths handed authority to someone else without asking this
/// question — granting a permission to a member, and assigning an application
/// role to one. The endpoint gate asks only <i>who may grant</i>
/// (<c>org:permissions:manage</c>), never <i>what may be granted</i>, so the
/// seeded <c>org-admin</c> role could hand any member — itself included — every
/// permission belonging to any application the organization has enabled,
/// including ones far above what it holds there.
/// </para>
/// <para>
/// <b>Why this cannot be <see cref="PermissionGrantGuard"/>.</b> That guard
/// reads the actor's <i>platform</i> permissions. An organization administrator
/// normally holds none at all, so reusing it here would refuse every
/// organization grant and read, from the outside, exactly like a working fix.
/// The question that belongs here is what the actor holds <i>inside this
/// organization, for this application</i>.
/// </para>
/// <para>
/// <b>Why the platform set is unioned in rather than bypassed.</b> A platform
/// operator administering an organization holds nothing scoped to it, so an
/// organization-only check would lock them out. Adding a "platform scope"
/// bypass flag would go too far the other way: a holder of
/// <c>organizations:manage</c> would then be able to hand out an application
/// permission it does not itself hold. Taking the union answers both — the
/// global <c>*</c> matches everything and passes, while narrower platform
/// authority still only grants what it actually carries.
/// </para>
/// <para>
/// <b>Read live, never from the token</b>, for the same reason as the platform
/// guard: claims are minted into the JWT and outlive a revocation, so an actor
/// whose authority was withdrawn a minute ago still carries a token attesting
/// to it. Grants are rare administrative operations; one extra query is the
/// correct price.
/// </para>
/// </remarks>
public class OrganizationGrantGuard
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IPermissionRepository _permissionRepository;

    public OrganizationGrantGuard(
        IOrganizationRepository organizationRepository,
        IPermissionRepository permissionRepository)
    {
        _organizationRepository = organizationRepository;
        _permissionRepository = permissionRepository;
    }

    /// <summary>
    /// Confirms the actor's own effective authority — inside this organization
    /// for this application, or platform-wide — covers every code it is trying
    /// to hand over.
    /// </summary>
    /// <remarks>
    /// Matching uses <see cref="PermissionCode.Matches"/>, the same wildcard
    /// semantics the authorization handler enforces, so a holder of
    /// <c>crm:*</c> may grant <c>crm:leads:read</c> but not <c>billing:read</c>,
    /// and a holder of the global <c>*</c> passes everything.
    /// </remarks>
    public async Task<ErrorOr<Success>> EnsureCanGrantAsync(
        Guid organizationId,
        Guid actorId,
        Guid applicationId,
        IEnumerable<string> requestedCodes,
        CancellationToken cancellationToken)
    {
        var requested = requestedCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
        {
            return Result.Success;
        }

        var withinOrganization = await _organizationRepository.GetEffectivePermissionCodesAsync(
            organizationId, actorId, applicationId, cancellationToken);

        var acrossPlatform = await _permissionRepository.GetUserEffectivePermissionsAsync(
            actorId, cancellationToken);

        var heldCodes = withinOrganization
            .Concat(acrossPlatform)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(PermissionCode.From)
            .ToList();

        foreach (var code in requested)
        {
            if (!heldCodes.Any(held => held.Matches(code)))
            {
                return PermissionErrors.CannotGrantHigherPermission;
            }
        }

        return Result.Success;
    }
}
