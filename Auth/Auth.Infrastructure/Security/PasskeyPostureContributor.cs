using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The posture of passkeys (S20, PK3, PK19): whether a switch-on can work, and whether live
/// passkeys are bound to an RP ID other than the one in force.
/// </summary>
/// <remarks>
/// <para>
/// <c>passkey.configuration</c> (A125): Critical while <c>Passkeys:Enabled</c> is on and the
/// RP ID, the first-party origins or the schema do not let passkeys run — the endpoints then
/// answer 404, so nobody can add or use one, and nothing else tells the operator why. Ok while
/// the switch is off, whatever the rest says: that is the shipped state.
/// </para>
/// <para>
/// <c>passkey.rpid-mismatch</c> (A124): Critical while any live passkey is bound to another RP
/// ID — an edit of <c>Passkeys:RpId</c> in a settings file. Such passkeys sign in nowhere and
/// count as no factor, so an administrator whose only factor was one is back to enrolling;
/// and passkeys stay off meanwhile (PK3: the new value is refused). The way out is to restore
/// the value, or retire the old RP ID ([S20-O10]).
/// </para>
/// <para>
/// Evaluated at start-up and after every configuration reload, re-reading the switch, the
/// origins and the schema each time. The rows are read in a scope of its own.
/// </para>
/// </remarks>
public sealed class PasskeyPostureContributor : ISecurityPostureContributor
{
    private const string ConfigurationTemplate =
        "{EventCode}: Passkeys:Enabled is on but passkeys cannot run, so every passkey endpoint answers 404: {Problem}";
    private const string MismatchTemplate =
        "{EventCode}: live passkeys are bound to an RP ID other than Passkeys:RpId '{RpId}': they no longer sign in or count as a second factor, and passkeys stay off until this is resolved. Restore the value, or retire the old RP ID";
    private const string StateTemplate =
        "Passkeys: {State}, RP ID '{RpId}'";

    private readonly IServiceScopeFactory _scopeFactory;

    public PasskeyPostureContributor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public string Name => "passkey";

    /// <inheritdoc />
    public async Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var feature = scope.ServiceProvider.GetRequiredService<PasskeyFeature>();
        var assessment = await feature.AssessConfigurationAsync(cancellationToken);

        var lines = new List<SecurityPostureLine>();
        var reasons = new List<string>();

        if (feature.IsSwitchedOn && !assessment.IsConfigured)
        {
            var problem = string.Join("; ", assessment.Problems);
            lines.Add(new SecurityPostureLine(LogLevel.Critical, ConfigurationTemplate, [PasskeySignalCodes.Configuration, problem]));
            reasons.Add($"{PasskeySignalCodes.Configuration}: {problem}");
        }

        // Without an RP ID or the schema there is nothing to compare: no passkey counts.
        var mismatch = feature.RpId.Length > 0
            && await scope.ServiceProvider.GetRequiredService<IPasskeySchema>().IsPresentAsync(cancellationToken)
            && await scope.ServiceProvider.GetRequiredService<IUserPasskeyRepository>()
                .HasLiveOfAnotherRpIdAsync(feature.RpId, cancellationToken);
        if (mismatch)
        {
            lines.Add(new SecurityPostureLine(LogLevel.Critical, MismatchTemplate, [PasskeySignalCodes.RpIdMismatch, feature.RpId]));
            reasons.Add($"{PasskeySignalCodes.RpIdMismatch}: live passkeys of another RP ID");
        }

        if (lines.Count > 0)
        {
            return new SecurityPosture(
                SecurityPostureLevel.Weak,
                reasons,
                lines,
                $"weak|{feature.IsSwitchedOn}|{assessment.IsConfigured}|{mismatch}|{string.Join(";", assessment.Problems)}");
        }

        var state = feature.IsSwitchedOn ? "on" : "off";
        return new SecurityPosture(
            SecurityPostureLevel.Ok,
            [],
            [new SecurityPostureLine(LogLevel.Information, StateTemplate, [state, feature.RpId])],
            $"ok|{state}|{feature.RpId}");
    }
}
