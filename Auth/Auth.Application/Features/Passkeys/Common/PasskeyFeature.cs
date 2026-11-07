using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// Whether passkeys run now (PK19), from the settings in force: <c>Passkeys:Enabled</c> and
/// the first-party origins, both read per call (hot), the RP ID the process started with,
/// and the schema. The one gate the passkey endpoints and handlers ask.
/// </summary>
/// <remarks>
/// <para>
/// It also decides whether a passkey can be USED as a factor right now (D-150-4, «off means
/// off»): while it says unavailable, <see cref="UsableSecondFactors"/> masks the passkey, so no
/// policy demands one or leans on one, and an administrator whose only factor is a passkey is
/// treated as before passkeys. The inventory (FA2) still holds the passkey: nothing is revoked,
/// and when passkeys run again every rule applies again.
/// </para>
/// <para>
/// A new RP ID is refused while live passkeys carry another (PK3): passkeys stay off, and the
/// posture names it, until the previous value is restored or the old passkeys are retired
/// ([S20-O10]). Otherwise people would add passkeys under a value the operator may still take
/// back, while the old ones count for nothing.
/// </para>
/// </remarks>
public sealed class PasskeyFeature
{
    private readonly IOptionsMonitor<PasskeySettings> _settings;
    private readonly IOptionsMonitor<IdentityProviderSettings> _identityProvider;
    private readonly PasskeyRpIdAtStartup _rpId;
    private readonly IPasskeySchema _schema;
    private readonly IUserPasskeyRepository _passkeys;

    // PK3's answer, read once per scope (REVIEW 150 F5): the 404 gate asks before
    // authentication and the handler asks again, and one request decides once.
    private bool? _otherRpIdLive;

    public PasskeyFeature(
        IOptionsMonitor<PasskeySettings> settings,
        IOptionsMonitor<IdentityProviderSettings> identityProvider,
        PasskeyRpIdAtStartup rpId,
        IPasskeySchema schema,
        IUserPasskeyRepository passkeys)
    {
        _settings = settings;
        _identityProvider = identityProvider;
        _rpId = rpId;
        _schema = schema;
        _passkeys = passkeys;
    }

    /// <summary>The RP ID the process started with: what every read of a passkey names.</summary>
    public string RpId => _rpId.Value;

    /// <summary>
    /// Gets whether the process started with an RP ID. Without one no passkey is "of the RP ID
    /// in force", so a read of them has nothing to find and is not made.
    /// </summary>
    public bool HasRpId => _rpId.IsSet;

    /// <summary>Assesses the configuration in force now.</summary>
    /// <remarks>
    /// While <c>Passkeys:Enabled</c> is off nothing else is read, so a deployment that never
    /// turns passkeys on never asks the database for the schema on a request.
    /// </remarks>
    public async Task<PasskeyConfigurationAssessment> AssessAsync(CancellationToken cancellationToken)
    {
        var enabled = _settings.CurrentValue.Enabled;
        if (!enabled)
        {
            return new PasskeyConfigurationAssessment(false, false, null, ["Passkeys:Enabled is off"]);
        }

        return await AssessConfigurationAsync(enabled, cancellationToken);
    }

    /// <summary>
    /// Assesses the configuration whatever the switch says: the posture reports a switch-on
    /// that cannot work, and a configuration that would.
    /// </summary>
    public async Task<PasskeyConfigurationAssessment> AssessConfigurationAsync(CancellationToken cancellationToken) =>
        await AssessConfigurationAsync(_settings.CurrentValue.Enabled, cancellationToken);

    /// <summary>The relying party when passkeys run now; null when they do not.</summary>
    public async Task<PasskeyRelyingParty?> RelyingPartyAsync(CancellationToken cancellationToken)
    {
        var assessment = await AssessAsync(cancellationToken);
        return assessment.IsAvailable ? assessment.RelyingParty : null;
    }

    /// <summary>Gets whether <c>Passkeys:Enabled</c> is on.</summary>
    public bool IsSwitchedOn => _settings.CurrentValue.Enabled;

    private async Task<PasskeyConfigurationAssessment> AssessConfigurationAsync(bool enabled, CancellationToken cancellationToken)
    {
        // Without an RP ID the feature cannot run whatever the schema holds: the database is
        // not asked, and the empty RP ID is the problem reported.
        var schemaPresent = !_rpId.IsSet || await _schema.IsPresentAsync(cancellationToken);
        var assessment = PasskeyConfiguration.Assess(
            enabled,
            _rpId.Value,
            _identityProvider.CurrentValue.FirstPartySpaOrigins,
            schemaPresent);

        // PK3: only a configuration that could otherwise run asks the rows, once per scope.
        if (!assessment.IsConfigured
            || !(_otherRpIdLive ??= await _passkeys.HasLiveOfAnotherRpIdAsync(_rpId.Value, cancellationToken)))
        {
            return assessment;
        }

        return new PasskeyConfigurationAssessment(
            IsAvailable: false,
            IsConfigured: false,
            RelyingParty: null,
            Problems: [PasskeyConfiguration.OtherRpIdLive]);
    }
}
