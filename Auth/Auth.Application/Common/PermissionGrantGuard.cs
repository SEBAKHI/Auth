using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Common;

/// <summary>
/// Enforces one rule across every path that hands authority to someone else:
/// <b>no principal may grant a permission it does not itself hold.</b>
/// </summary>
/// <remarks>
/// <para>
/// Without it, "who may grant" and "what may be granted" are separate questions
/// and only the first is asked. A role holding <c>users:manage-permissions</c>
/// passes the endpoint gate and may then hand out <b>any</b> permission row in
/// the table, including the global <c>*</c> — so the holder can promote itself
/// to super-administrator in one call. The same applies to assigning a role:
/// the role's permissions transfer wholesale, so assigning a more powerful role
/// is granting its permissions by another name.
/// </para>
/// <para>
/// This has been latent rather than exploitable only because the platform
/// permission codes were never seeded, which left every built-in role holding
/// nothing. It becomes live the moment they are, which is why it lands first
/// and in its own deployment.
/// </para>
/// <para>
/// <b>Read live, never from the token.</b> Permissions are baked into the JWT
/// when it is minted and stay valid until it expires, so an actor whose
/// authority was revoked a minute ago still carries a token that attests to it.
/// Trusting the claim would let that actor re-issue the revoked permission to
/// another account, where it outlives the revocation. One extra query per grant
/// is the correct price; grants are rare administrative operations.
/// </para>
/// <para>
/// The organization-scoped equivalent already existed as
/// <c>OrganizationErrors.CannotAssignOwnerRole</c>. So did this error and all
/// seven of its translations — <see cref="PermissionErrors.CannotGrantHigherPermission"/>
/// was written, localized, and never thrown by anything.
/// </para>
/// </remarks>
public class PermissionGrantGuard
{
    private readonly IPermissionRepository _permissionRepository;

    public PermissionGrantGuard(IPermissionRepository permissionRepository)
    {
        _permissionRepository = permissionRepository;
    }

    /// <summary>
    /// Confirms the actor's own effective permissions cover every code it is
    /// trying to hand over.
    /// </summary>
    /// <remarks>
    /// Matching uses <see cref="PermissionCode.Matches"/>, the same wildcard
    /// semantics the authorization handler enforces, so a holder of
    /// <c>users:*</c> may grant <c>users:read</c> but not <c>roles:read</c>, and
    /// a holder of <c>*</c> passes everything — which is what keeps the platform
    /// administrable and must never be narrowed.
    /// </remarks>
    public async Task<ErrorOr<Success>> EnsureCanGrantAsync(
        Guid actorId,
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

        var held = await _permissionRepository.GetUserEffectivePermissionsAsync(
            actorId, cancellationToken);

        var heldCodes = held.Select(PermissionCode.From).ToList();

        foreach (var code in requested)
        {
            if (!heldCodes.Any(h => h.Matches(code)))
            {
                return PermissionErrors.CannotGrantHigherPermission;
            }
        }

        return Result.Success;
    }
}
