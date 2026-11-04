using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Domain.Constants;
using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The token revocation list (X04, RV1 and RV2 (e)): whether it was loaded at startup, and
/// whether the access-token lifetime or the clock skew is configured outside the bounds it is
/// read within.
/// </summary>
/// <remarks>
/// <para>
/// Registered after the revocation background service, whose start awaits the first load, so
/// the posture written after startup already knows how that load ended.
/// </para>
/// <para>
/// The state is built from the reasons alone, never from a count or an elapsed time, so a
/// reload that changes nothing here writes nothing.
/// </para>
/// </remarks>
public sealed class RevocationPostureContributor : ISecurityPostureContributor
{
    /// <summary>A lifetime or skew outside its bounds was brought inside them.</summary>
    public const string LifetimeClampedReason = "revocation.lifetime-clamped";

    /// <summary>No load of the revocation list has succeeded since the process started.</summary>
    public const string RehydrationFailedReason = RevocationSignalCodes.RehydrationFailed;

    private const string ClampedTemplate =
        "Jwt:AccessTokenLifetimeMinutes = {ConfiguredLifetime} or Jwt:ClockSkewSeconds = {ConfiguredSkew} is outside {MinLifetime}–{MaxLifetime} minutes and {MinSkew}–{MaxSkew} seconds: tokens are issued and validated with {Lifetime} minutes and {Skew} seconds. Set a value within the bounds";
    private const string NotLoadedTemplate =
        "{EventCode}: the token revocation list is not loaded: an access token revoked before this start is accepted again until a load succeeds (retried every 30 s; /ready reports it)";
    private const string OkTemplate =
        "Token revocation list loaded; a user-level revocation is kept {HorizonMinutes} minutes";

    private readonly RevocationRehydrationStatus _status;
    private readonly IOptionsMonitor<JwtSettings> _jwtSettings;

    public RevocationPostureContributor(RevocationRehydrationStatus status, IOptionsMonitor<JwtSettings> jwtSettings)
    {
        _status = status;
        _jwtSettings = jwtSettings;
    }

    /// <inheritdoc />
    public string Name => "revocation";

    /// <inheritdoc />
    public Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        var jwt = _jwtSettings.CurrentValue;
        var reasons = new List<string>();
        var lines = new List<SecurityPostureLine>();

        if (jwt.AccessTokenLifetimeMinutes is < JwtSettings.MinAccessTokenLifetimeMinutes or > JwtSettings.MaxAccessTokenLifetimeMinutes
            || jwt.ClockSkewSeconds is < JwtSettings.MinClockSkewSeconds or > JwtSettings.MaxClockSkewSeconds)
        {
            reasons.Add(LifetimeClampedReason);
            lines.Add(new SecurityPostureLine(LogLevel.Warning, ClampedTemplate,
            [
                jwt.AccessTokenLifetimeMinutes, jwt.ClockSkewSeconds,
                JwtSettings.MinAccessTokenLifetimeMinutes, JwtSettings.MaxAccessTokenLifetimeMinutes,
                JwtSettings.MinClockSkewSeconds, JwtSettings.MaxClockSkewSeconds,
                (int)jwt.AccessTokenLifetime.TotalMinutes, (int)jwt.ClockSkew.TotalSeconds
            ]));
        }

        if (!_status.IsLoaded)
        {
            reasons.Add(RehydrationFailedReason);
            lines.Add(new SecurityPostureLine(LogLevel.Critical, NotLoadedTemplate, [RevocationSignalCodes.RehydrationFailed]));
        }

        if (reasons.Count == 0)
        {
            return Task.FromResult(new SecurityPosture(
                SecurityPostureLevel.Ok,
                [],
                [new SecurityPostureLine(LogLevel.Information, OkTemplate, [(int)RevocationHorizon.User.TotalMinutes])],
                "ok"));
        }

        return Task.FromResult(new SecurityPosture(
            SecurityPostureLevel.Weak, reasons, lines, string.Join(",", reasons)));
    }
}
