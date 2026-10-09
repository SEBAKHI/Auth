using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S08 T1/T2: the one decision on whether a platform administrator's token carries
/// platform authority. Platform permissions × methods × factor × switch, in the
/// fixed order of M7, and the guarantees around it: org_perm is never withheld,
/// application tokens are never touched, and with the switch off the claims are
/// exactly the ones resolved.
/// </summary>
public class PlatformMfaPolicyTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OrganizationId = Guid.NewGuid();

    private static readonly TokenClaims Admin = new(
        ["super-admin"], ["*"], [(OrganizationId, "org:members:read")]);

    private static readonly TokenClaims PlainUser = new(
        ["user"], [], [(OrganizationId, "org:members:read")]);

    private static readonly AuthenticationMethods Pwd = AuthenticationMethods.Password;
    private static readonly AuthenticationMethods Mfa = AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

    private readonly Mock<ITwoFactorStateStore> _store = new(MockBehavior.Strict);
    private readonly Mock<ITokenClaimsResolver> _resolver = new(MockBehavior.Strict);
    private readonly Mock<ILogger<PlatformMfaPolicy>> _logger = new();
    private readonly TwoFactorSettings _settings = new();

    private PlatformMfaPolicy Policy() =>
        new(_store.Object, _resolver.Object, TestHelpers.CreateOptions(_settings), TestHelpers.LoadedSettingsReloader(), new EnforcedSettingsWarning(), _logger.Object);

    private void GivenFactor(bool enabled) =>
        _store.Setup(s => s.HasEnabledFactorAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(enabled);

    private void VerifyLog(LogLevel level, string text, Times times) =>
        _logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(text)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    // ── Apply: the matrix ────────────────────────────────────────────────────

    public static TheoryData<string, bool, AuthenticationMethods, MfaRequirement> Assessments => new()
    {
        // case, account has an enabled factor, session methods, requirement
        { "no factor, password", false, Pwd, MfaRequirement.Enroll },
        { "no factor, MFA session (factor removed since)", false, Mfa, MfaRequirement.Enroll },
        { "no factor, unknown", false, AuthenticationMethods.Unknown, MfaRequirement.Enroll },
        { "factor, MFA session", true, Mfa, MfaRequirement.None },
        { "factor, recovery-code session", true, Pwd.With(AuthenticationMethods.RecoveryCode), MfaRequirement.None },
        { "factor, password", true, Pwd, MfaRequirement.StepUp },
        { "factor, external identity", true, AuthenticationMethods.ExternalIdentity, MfaRequirement.StepUp },
        { "factor, unknown (legacy session)", true, AuthenticationMethods.Unknown, MfaRequirement.Reauthenticate },
        { "factor, email code first", true, AuthenticationMethods.EmailCode, MfaRequirement.Reauthenticate },
        { "factor, email code + totp", true, AuthenticationMethods.EmailCode.With(AuthenticationMethods.Totp), MfaRequirement.Reauthenticate },
        { "factor, totp alone", true, AuthenticationMethods.Totp, MfaRequirement.Reauthenticate },
    };

    [Theory]
    [MemberData(nameof(Assessments))]
    public void Apply_Enforced_WithholdsPlatformAuthority_KeepsOrgPerm(
        string name, bool hasFactor, AuthenticationMethods methods, MfaRequirement expected)
    {
        var decision = Policy().Apply(Admin, methods, hasFactor, enforce: true);

        decision.Requirement.Should().Be(expected, name);
        decision.Assessed.Should().Be(expected, name);

        if (expected == MfaRequirement.None)
        {
            decision.Claims.Should().BeSameAs(Admin, "a session that proved two factors keeps everything");
            decision.Withheld.Should().BeFalse();
            return;
        }

        decision.Withheld.Should().BeTrue();
        decision.Claims.Permissions.Should().BeEmpty(name);
        decision.Claims.RoleCodes.Should().BeEmpty(name);
        decision.Claims.OrganizationPermissions.Should().Equal(Admin.OrganizationPermissions,
            "org_perm is not platform authority and is never withheld");
    }

    [Theory]
    [MemberData(nameof(Assessments))]
    public void Apply_SwitchOff_ChangesNothing_ButStillAssesses(
        string name, bool hasFactor, AuthenticationMethods methods, MfaRequirement expected)
    {
        var decision = Policy().Apply(Admin, methods, hasFactor, enforce: false);

        decision.Claims.Should().BeSameAs(Admin, name);
        decision.Requirement.Should().Be(MfaRequirement.None, "with the switch off nothing is in force");
        decision.Assessed.Should().Be(expected, "the readiness log reports what enforcement would ask");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_NoPlatformPermission_IsNeverTouched(bool enforce)
    {
        // An ordinary account — organization permissions only — whatever its
        // session proved and whether or not it has a factor: two-step stays optional.
        foreach (var hasFactor in new[] { true, false })
        {
            var decision = Policy().Apply(PlainUser, AuthenticationMethods.Unknown, hasFactor, enforce);

            decision.Claims.Should().BeSameAs(PlainUser);
            decision.Requirement.Should().Be(MfaRequirement.None);
            decision.Assessed.Should().Be(MfaRequirement.None);
        }
    }

    [Fact]
    public void Apply_EnrollComesBeforeTheSession()
    {
        // The break: checking IsMfaSatisfied before "has a factor" would let a
        // session that proved a factor since removed keep platform authority.
        var decision = Policy().Apply(Admin, Mfa, hasEnabledSecondFactor: false, enforce: true);

        decision.Requirement.Should().Be(MfaRequirement.Enroll);
        decision.Claims.Permissions.Should().BeEmpty();
    }

    // ── EvaluateAsync: what is read, and when ───────────────────────────────

    [Fact]
    public async Task EvaluateAsync_ApplicationToken_IsReturnedUntouched_WithoutAnyRead()
    {
        _settings.EnforceForPlatformAdmins = true;

        var decision = await Policy().EvaluateAsync(UserId, Guid.NewGuid(), Admin, Pwd, CancellationToken.None);

        decision.Claims.Should().BeSameAs(Admin, "an application token is never withheld from (owner rule)");
        decision.Requirement.Should().Be(MfaRequirement.None);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluateAsync_NoPlatformPermission_ReadsNothing()
    {
        _settings.EnforceForPlatformAdmins = true;

        var decision = await Policy().EvaluateAsync(UserId, null, PlainUser, AuthenticationMethods.Unknown, CancellationToken.None);

        decision.Should().BeEquivalentTo(PlatformMfaDecision.Unchanged(PlainUser));
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluateAsync_Enforced_WithholdsAndLogsIt()
    {
        _settings.EnforceForPlatformAdmins = true;
        GivenFactor(true);

        var decision = await Policy().EvaluateAsync(UserId, null, Admin, Pwd, CancellationToken.None);

        decision.Requirement.Should().Be(MfaRequirement.StepUp);
        decision.Claims.Permissions.Should().BeEmpty();
        VerifyLog(LogLevel.Information, "PlatformMfa.Withheld", Times.Once());
        VerifyLog(LogLevel.Warning, "PlatformMfa.WouldRequire", Times.Never());
    }

    [Fact]
    public async Task EvaluateAsync_SwitchOff_KeepsClaims_AndLogsWouldRequire()
    {
        GivenFactor(false);

        var decision = await Policy().EvaluateAsync(UserId, null, Admin, Pwd, CancellationToken.None);

        decision.Claims.Should().BeSameAs(Admin);
        decision.Requirement.Should().Be(MfaRequirement.None);
        decision.Assessed.Should().Be(MfaRequirement.Enroll);
        VerifyLog(LogLevel.Warning, "PlatformMfa.WouldRequire", Times.Once());
    }

    [Fact]
    public async Task EvaluateAsync_SwitchOff_MfaSession_LogsNothing()
    {
        GivenFactor(true);

        await Policy().EvaluateAsync(UserId, null, Admin, Mfa, CancellationToken.None);

        VerifyLog(LogLevel.Warning, "PlatformMfa", Times.Never());
        VerifyLog(LogLevel.Information, "PlatformMfa", Times.Never());
    }

    [Fact]
    public async Task EvaluateAsync_SwitchOff_FactorReadFails_SignInIsUnaffected()
    {
        // The read serves only the readiness log while the switch is off: its
        // failure must not cost a sign-in that never needed it.
        _store.Setup(s => s.HasEnabledFactorAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var decision = await Policy().EvaluateAsync(UserId, null, Admin, Pwd, CancellationToken.None);

        decision.Should().BeEquivalentTo(PlatformMfaDecision.Unchanged(Admin));
        VerifyLog(LogLevel.Warning, "could not be assessed", Times.Once());
    }

    [Fact]
    public async Task EvaluateAsync_Enforced_FactorReadFails_Propagates()
    {
        // Under enforcement a token is never minted on a guess.
        _settings.EnforceForPlatformAdmins = true;
        _store.Setup(s => s.HasEnabledFactorAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => Policy().EvaluateAsync(UserId, null, Admin, Pwd, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EvaluateAsync_ReadsTheSwitchLive()
    {
        GivenFactor(false);
        var policy = Policy();

        (await policy.EvaluateAsync(UserId, null, Admin, Pwd, CancellationToken.None)).Withheld.Should().BeFalse();

        _settings.EnforceForPlatformAdmins = true;
        (await policy.EvaluateAsync(UserId, null, Admin, Pwd, CancellationToken.None)).Withheld.Should().BeTrue(
            "the switch is hot: read at every mint, never cached");
    }

    // ── IsEnforcedFor: the disable rule ──────────────────────────────────────

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void IsEnforcedFor_NeedsTheSwitchAndAPlatformPermission(bool enforce, bool admin, bool expected)
    {
        _settings.EnforceForPlatformAdmins = enforce;

        Policy().IsEnforcedFor(admin ? Admin : PlainUser).Should().Be(expected);
    }

    [Fact]
    public async Task IsEnforcedForUserAsync_SwitchOff_ResolvesNothing()
    {
        (await Policy().IsEnforcedForUserAsync(UserId, CancellationToken.None)).Should().BeFalse();
        _resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IsEnforcedForUserAsync_SwitchOn_ReadsThePlatformClaims()
    {
        _settings.EnforceForPlatformAdmins = true;
        _resolver.Setup(r => r.ResolveAsync(UserId, null, It.IsAny<CancellationToken>())).ReturnsAsync(Admin);

        (await Policy().IsEnforcedForUserAsync(UserId, CancellationToken.None)).Should().BeTrue();
    }
}
