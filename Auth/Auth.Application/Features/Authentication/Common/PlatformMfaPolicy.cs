using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <inheritdoc />
/// <remarks>
/// <para>
/// Why withhold rather than refuse the sign-in: a refused sign-in would leave an
/// administrator with no factor no way to set one up. With the authority withheld
/// the session still works for everything an ordinary account may do — its own
/// profile and two-factor endpoints first of all — and the consoles open the
/// page that enrols or steps up. Every <c>[RequirePermission]</c> then refuses by
/// construction, because the permission is not in the token.
/// </para>
/// <para>
/// "The account has a factor" is read from the two-factor row, not from the
/// account flag the sign-in gate reads: an account whose flag says off while its
/// row is enabled steps up (which it can), instead of enrolling (which setup
/// refuses for an enabled row).
/// </para>
/// <para>
/// The switch fails closed: until the process has read the database settings
/// once, it does not know what an administrator saved there, and enforces. A
/// failed refresh later keeps the values last read, so only the window before
/// the first good read is affected (at most one refresh interval).
/// </para>
/// </remarks>
public class PlatformMfaPolicy : IPlatformMfaPolicy
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITokenClaimsResolver _tokenClaimsResolver;
    private readonly IOptionsMonitor<TwoFactorSettings> _settings;
    private readonly ISystemSettingsReloader _settingsReloader;
    private readonly ILogger<PlatformMfaPolicy> _logger;

    // One warning per request is enough: a request can read the switch several
    // times (a mint, a grant, a disable), and the window lasts minutes at most.
    private bool _settingsUnavailableLogged;

    public PlatformMfaPolicy(
        ITwoFactorStateStore twoFactorStateStore,
        ITokenClaimsResolver tokenClaimsResolver,
        IOptionsMonitor<TwoFactorSettings> settings,
        ISystemSettingsReloader settingsReloader,
        ILogger<PlatformMfaPolicy> logger)
    {
        _twoFactorStateStore = twoFactorStateStore;
        _tokenClaimsResolver = tokenClaimsResolver;
        _settings = settings;
        _settingsReloader = settingsReloader;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnforcing
    {
        get
        {
            if (_settings.CurrentValue.EnforceForPlatformAdmins)
            {
                return true;
            }

            if (_settingsReloader.HasLoadedSinceStart)
            {
                return false;
            }

            // The files say off, but what an administrator saved in the database
            // has never been read: it may say on. Enforce until it is read.
            if (!_settingsUnavailableLogged)
            {
                _settingsUnavailableLogged = true;
                _logger.LogWarning(
                    "PlatformMfa.EnforcedSettingsUnavailable: TwoFactor:EnforceForPlatformAdmins is enforced because the database settings have not loaded since the API started; the saved value applies from the next successful load");
            }

            return true;
        }
    }

    /// <inheritdoc />
    public PlatformMfaDecision Apply(
        TokenClaims platformClaims,
        AuthenticationMethods methods,
        bool hasEnabledSecondFactor,
        bool enforce)
    {
        var assessed = Assess(platformClaims, methods, hasEnabledSecondFactor);

        if (assessed == MfaRequirement.None || !enforce)
        {
            return new PlatformMfaDecision(platformClaims, MfaRequirement.None, assessed);
        }

        // Platform authority only. The organization claims are not platform
        // authority and stay, so the session keeps whatever its memberships give.
        var withheld = platformClaims with { RoleCodes = [], Permissions = [] };
        return new PlatformMfaDecision(withheld, assessed, assessed);
    }

    /// <inheritdoc />
    public async Task<PlatformMfaDecision> EvaluateAsync(
        Guid userId,
        Guid? applicationId,
        TokenClaims claims,
        AuthenticationMethods methods,
        CancellationToken cancellationToken)
    {
        // An application token carries that application's grants, never platform
        // authority, and a user without platform permissions has nothing to
        // withhold: neither reads anything.
        if (applicationId is not null || claims.Permissions.Count == 0)
        {
            return PlatformMfaDecision.Unchanged(claims);
        }

        // Read once: the switch is hot, and one mint decides once.
        var enforce = IsEnforcing;

        bool hasEnabledSecondFactor;
        try
        {
            hasEnabledSecondFactor = await _twoFactorStateStore.HasEnabledFactorAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (!enforce && ex is not OperationCanceledException)
        {
            // With the switch off the read serves only the readiness log below; a
            // failure here must not cost a sign-in that never needed it. With the
            // switch on it propagates and the mint fails, as any failed read on
            // this path does: a token is never minted on a guess.
            _logger.LogWarning(ex,
                "PlatformMfa.WouldRequire could not be assessed for user {UserId}: the two-factor row could not be read",
                userId);
            return PlatformMfaDecision.Unchanged(claims);
        }

        var decision = Apply(claims, methods, hasEnabledSecondFactor, enforce);

        if (decision.Withheld)
        {
            _logger.LogInformation(
                "PlatformMfa.Withheld: platform permissions withheld from user {UserId} until the session completes {Requirement} (methods {AuthMethods})",
                userId, decision.Requirement.ToClaimValue(), methods.Value);
        }
        else if (decision.Assessed != MfaRequirement.None)
        {
            // The readiness measure before the switch is turned on: every line is
            // an administrator session that enforcement would hold back.
            _logger.LogWarning(
                "PlatformMfa.WouldRequire: user {UserId} holds platform permissions and this session would need {Requirement} (methods {AuthMethods}) if TwoFactor:EnforceForPlatformAdmins were on",
                userId, decision.Assessed.ToClaimValue(), methods.Value);
        }

        return decision;
    }

    /// <inheritdoc />
    public bool IsEnforcedFor(TokenClaims platformClaims) =>
        platformClaims.Permissions.Count > 0 && IsEnforcing;

    /// <inheritdoc />
    public async Task<bool> IsEnforcedForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!IsEnforcing)
        {
            return false;
        }

        var platformClaims = await _tokenClaimsResolver.ResolveAsync(userId, applicationId: null, cancellationToken);
        return IsEnforcedFor(platformClaims);
    }

    /// <summary>
    /// The fixed order of the decision. Enroll comes before the session is looked
    /// at: a session that proved a factor which has since been removed must not
    /// keep the authority on the strength of what it once proved.
    /// </summary>
    private static MfaRequirement Assess(
        TokenClaims platformClaims,
        AuthenticationMethods methods,
        bool hasEnabledSecondFactor)
    {
        if (platformClaims.Permissions.Count == 0)
        {
            return MfaRequirement.None;
        }

        if (!hasEnabledSecondFactor)
        {
            return MfaRequirement.Enroll;
        }

        if (methods.IsMfaSatisfied)
        {
            return MfaRequirement.None;
        }

        return methods.HasPrimary
            ? MfaRequirement.StepUp
            : MfaRequirement.Reauthenticate;
    }
}
