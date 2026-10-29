using Auth.Application.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.ApiKeys.Common;

/// <summary>
/// What a key being minted may carry (OI-128 PR B), asked the same way at creation and at
/// rotation: every scope resolves, the key's application may carry them all
/// (<see cref="ApiKey.EnsureCanCarry"/>), and the administrator minting the key holds each one
/// (<see cref="PermissionGrantGuard"/>).
/// </summary>
/// <remarks>
/// Rotation mints a fresh plain key with the old key's scopes. Without the grant guard there, an
/// administrator holding <c>apikeys:rotate</c> but not a scope would receive a new key that
/// carries it.
/// </remarks>
public sealed class ApiKeyScopePolicy
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly PermissionGrantGuard _grantGuard;

    public ApiKeyScopePolicy(IPermissionRepository permissionRepository, PermissionGrantGuard grantGuard)
    {
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
    }

    /// <summary>
    /// The distinct permission ids a key of <paramref name="applicationId"/>, minted by
    /// <paramref name="actorId"/>, may carry; or the first refusal.
    /// </summary>
    public async Task<ErrorOr<IReadOnlyList<Guid>>> ResolveAsync(
        Guid applicationId,
        IEnumerable<Guid> permissionIds,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        // Resolve every requested scope before anything is written, and refuse an id that does
        // not resolve rather than dropping it: a key with fewer scopes than asked for, and
        // nothing in the answer to say so, is worse than a refusal.
        var permissions = new List<Permission>();
        foreach (var permissionId in permissionIds.Distinct())
        {
            var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
            if (permission is null)
            {
                return PermissionErrors.NotFound(permissionId);
            }

            permissions.Add(permission);
        }

        var carried = ApiKey.EnsureCanCarry(applicationId, permissions);
        if (carried.IsError)
        {
            return carried.Errors;
        }

        // No amplification. An API key is a credential in its own right, carried by relying
        // parties through the SDK, so scoping one past what its minter holds hands out authority
        // that minter could not have granted directly.
        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            actorId, permissions.Select(permission => permission.Code.Value), cancellationToken);
        if (canGrant.IsError)
        {
            return canGrant.Errors;
        }

        return permissions.Select(permission => permission.Id).ToList();
    }
}
