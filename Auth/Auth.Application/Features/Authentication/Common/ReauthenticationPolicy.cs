using Auth.Application.Configuration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Reads what a signed-in account must prove to prove itself again
/// (<see cref="ReauthenticationRequirement"/>): the one read behind the window that asks and the
/// requests that answer it, so they cannot disagree.
/// </summary>
public sealed class ReauthenticationPolicy
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly UsableSecondFactors _usableFactors;
    private readonly PasskeyOnlyAdministratorRule _passkeyOnlyAdministrator;
    private readonly EmailSettings _emailSettings;

    public ReauthenticationPolicy(
        ITwoFactorStateStore twoFactorStateStore,
        UsableSecondFactors usableFactors,
        PasskeyOnlyAdministratorRule passkeyOnlyAdministrator,
        IOptionsSnapshot<EmailSettings> emailSettings)
    {
        _twoFactorStateStore = twoFactorStateStore;
        _usableFactors = usableFactors;
        _passkeyOnlyAdministrator = passkeyOnlyAdministrator;
        // A snapshot, read in this request's scope: Email:Enabled is hot.
        _emailSettings = emailSettings.Value;
    }

    /// <summary>What <paramref name="user"/> must prove, as the account stands now.</summary>
    public async Task<ReauthenticationRequirement> ForAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var factor = await _twoFactorStateStore.GetSnapshotAsync(user.Id, cancellationToken);
        var usable = await _usableFactors.GetAsync(user.Id, cancellationToken);
        var passkeyOnlyAdministrator = await _passkeyOnlyAdministrator.AppliesToAsync(user.Id, cancellationToken);

        return ReauthenticationRequirement.For(
            hasPassword: user.PasswordHash is not null,
            emailConfirmed: user.EmailConfirmed,
            factor,
            emailOn: _emailSettings.Enabled,
            passkeyUsable: usable.Passkey,
            passkeyOnlyAdministrator,
            signInGateOn: user.TwoFactorEnabled);
    }
}
