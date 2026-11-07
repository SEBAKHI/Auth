using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// The second factors an account can USE right now (S20, D-150-4 «off means off», T-B6): the
/// inventory (FA2, <see cref="ITwoFactorStateStore.GetQualifyingFactorsAsync"/>) with the passkey
/// masked while passkeys do not run — the very assessment that answers 404
/// (<see cref="PasskeyFeature.AssessAsync"/>). The one place the mask is applied.
/// </summary>
/// <remarks>
/// <para>
/// While passkeys are switched off, or cannot run, no rule demands a passkey or leans on one:
/// the policies see none, setting up the app treats the account as having no factor, and the
/// refusal of a passkey-only administrator at the other doors pauses. Nothing is revoked; when
/// passkeys run again every rule applies again.
/// </para>
/// <para>
/// The readers that must see what the account holds whatever the switch says keep the
/// inventory: switching the app off while email is off (D-150-5, N13), the administrator's reset
/// (M8(4)), the console's user page and the passkey routes themselves.
/// </para>
/// <para>
/// While <c>Passkeys:Enabled</c> is off the assessment reads nothing, so a deployment that never
/// turns passkeys on pays for no extra statement.
/// </para>
/// </remarks>
public sealed class UsableSecondFactors
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly PasskeyFeature _feature;

    public UsableSecondFactors(ITwoFactorStateStore twoFactorStateStore, PasskeyFeature feature)
    {
        _twoFactorStateStore = twoFactorStateStore;
        _feature = feature;
    }

    /// <summary>Whether a passkey counts as a factor right now: whether passkeys run (PK19).</summary>
    public async Task<bool> PasskeysCountAsync(CancellationToken cancellationToken) =>
        (await _feature.AssessAsync(cancellationToken)).IsAvailable;

    /// <summary>The account's usable factors.</summary>
    public async Task<QualifyingSecondFactors> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        await GetAsync(userId, await PasskeysCountAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// The account's usable factors, for a caller that already asked
    /// <see cref="PasskeysCountAsync"/> and must know its answer before the read.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="passkeysCount">The answer of <see cref="PasskeysCountAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<QualifyingSecondFactors> GetAsync(Guid userId, bool passkeysCount, CancellationToken cancellationToken) =>
        Mask(await _twoFactorStateStore.GetQualifyingFactorsAsync(userId, cancellationToken), passkeysCount);

    /// <summary>The inventory as it can be used: without the passkey when passkeys do not count.</summary>
    private static QualifyingSecondFactors Mask(QualifyingSecondFactors inventory, bool passkeysCount) =>
        passkeysCount ? inventory : inventory with { Passkey = false };
}
