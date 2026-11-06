using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B, D-S20-4 (ii): the platform policy, per account. An account whose only usable
/// second factor is a passkey holds platform authority only through a passkey session, whatever
/// <c>TwoFactor:EnforceForPlatformAdmins</c> says: <c>enforce = IsEnforcing || usable.IsPasskeyOnly</c>.
/// Every other account is decided by the switch, as before. While passkeys do not run the
/// passkey is no usable factor (D-150-4), so such an account is decided as one with no factor.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>enforce</c> read from the switch alone (the
/// passkey-only rows keep their authority); the inventory read in place of the usable factors
/// (the passkeys-off row withholds); the failed read swallowed whenever the switch is off (the
/// read that can withhold propagates no more).
/// </remarks>
public class PasskeyOnlyAdministratorPolicyTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly TokenClaims Admin = new(["super-admin"], ["*"], []);
    private static readonly AuthenticationMethods PasswordSession = AuthenticationMethods.Password;
    private static readonly AuthenticationMethods PasskeySession =
        AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);

    private static readonly QualifyingSecondFactors PasskeyOnly = new(Totp: false, Passkey: true);
    private static readonly QualifyingSecondFactors Both = new(Totp: true, Passkey: true);
    private static readonly QualifyingSecondFactors AppOnly = new(Totp: true, Passkey: false);

    private readonly Mock<ITwoFactorStateStore> _store = new(MockBehavior.Strict);
    private readonly CapturingLogger<PlatformMfaPolicy> _log = new();

    public static TheoryData<string, QualifyingSecondFactors, bool, bool, AuthenticationMethods, MfaRequirement> Matrix => new()
    {
        // Because, the inventory, the switch, whether passkeys run, the session, the requirement
        // the decision withholds for (None: nothing withheld).
        { "passkey-only, switch off, password session", PasskeyOnly, false, true, PasswordSession, MfaRequirement.Passkey },
        { "passkey-only, switch on, password session", PasskeyOnly, true, true, PasswordSession, MfaRequirement.Passkey },
        { "passkey-only, switch off, passkey session", PasskeyOnly, false, true, PasskeySession, MfaRequirement.None },
        { "passkey-only, switch on, passkey session", PasskeyOnly, true, true, PasskeySession, MfaRequirement.None },
        { "passkey-only while passkeys do not run, switch off: off means off", PasskeyOnly, false, false, PasswordSession, MfaRequirement.None },
        { "passkey-only while passkeys do not run, switch on: no usable factor", PasskeyOnly, true, false, PasswordSession, MfaRequirement.Enroll },
        { "app and passkey, switch off: the switch decides", Both, false, true, PasswordSession, MfaRequirement.None },
        { "app and passkey, switch on", Both, true, true, PasswordSession, MfaRequirement.StepUp },
        { "app alone, switch off", AppOnly, false, true, PasswordSession, MfaRequirement.None },
        { "app alone, switch on", AppOnly, true, true, PasswordSession, MfaRequirement.StepUp },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task EvaluateAsync_DecidesPerAccount(
        string because, QualifyingSecondFactors inventory, bool switchedOn, bool passkeysRun, AuthenticationMethods session,
        MfaRequirement withheldFor)
    {
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(inventory);

        var decision = await Policy(switchedOn, passkeysRun).EvaluateAsync(UserId, null, Admin, session, CancellationToken.None);

        decision.Requirement.Should().Be(withheldFor, because);
        if (withheldFor == MfaRequirement.None)
        {
            decision.Claims.Should().Be(Admin, because);
        }
        else
        {
            decision.Claims.Permissions.Should().BeEmpty(because);
            decision.Claims.RoleCodes.Should().BeEmpty(because);
        }
    }

    [Fact]
    public async Task EvaluateAsync_PasskeyOnlyPasswordSession_NamesThePasskeyInTheClaimAndTheLog()
    {
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(PasskeyOnly);

        var decision = await Policy(switchedOn: false, passkeysRun: true).EvaluateAsync(UserId, null, Admin, PasswordSession, CancellationToken.None);

        decision.Requirement.ToClaimValue().Should().Be(MfaRequirementValues.Passkey, "the mfa_req claim the console reads");
        _log.Entries.Should().ContainSingle(entry => entry.Message.Contains("PlatformMfa.Withheld", StringComparison.Ordinal)
            && entry.Values.Contains(MfaRequirementValues.Passkey));
    }

    [Fact]
    public async Task EvaluateAsync_ApplicationToken_IsNeverTouched_EvenForAPasskeyOnlyAccount()
    {
        var decision = await Policy(switchedOn: false, passkeysRun: true)
            .EvaluateAsync(UserId, Guid.NewGuid(), Admin, PasswordSession, CancellationToken.None);

        decision.Withheld.Should().BeFalse("an application token carries no platform authority");
        _store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("switch off, passkeys run: a usable passkey could withhold", false, true)]
    [InlineData("switch on", true, true)]
    [InlineData("switch on, passkeys do not run", true, false)]
    public async Task EvaluateAsync_FailedReadThatCouldWithhold_Propagates(string because, bool switchedOn, bool passkeysRun)
    {
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("the factor store did not answer"));

        var act = () => Policy(switchedOn, passkeysRun).EvaluateAsync(UserId, null, Admin, PasswordSession, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>(because);
    }

    [Fact]
    public async Task EvaluateAsync_FailedReadThatCannotWithhold_IsSwallowed()
    {
        // Switch off and no passkey counting: the read serves only the readiness log.
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("the factor store did not answer"));

        var decision = await Policy(switchedOn: false, passkeysRun: false).EvaluateAsync(UserId, null, Admin, PasswordSession, CancellationToken.None);

        decision.Should().Be(PlatformMfaDecision.Unchanged(Admin));
    }

    public static TheoryData<string, QualifyingSecondFactors, bool, AuthenticationMethods> AccountsWithoutAPasskey => new()
    {
        // Because, the inventory, the switch, the session.
        { "app alone, switch off, password", AppOnly, false, PasswordSession },
        { "app alone, switch on, password", AppOnly, true, PasswordSession },
        { "app alone, switch on, password and app", AppOnly, true, PasswordSession.With(AuthenticationMethods.Totp) },
        { "no factor, switch off", QualifyingSecondFactors.None, false, PasswordSession },
        { "no factor, switch on", QualifyingSecondFactors.None, true, PasswordSession },
    };

    [Theory]
    [MemberData(nameof(AccountsWithoutAPasskey))]
    public async Task EvaluateAsync_AccountWithoutAPasskey_GetsTodaysAnswer_WhetherPasskeysRunOrNot(
        string because, QualifyingSecondFactors factors, bool switchedOn, AuthenticationMethods session)
    {
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(factors);

        // Today's answer: the switch alone decides (before PR B).
        var today = Policy(switchedOn, passkeysRun: false).Apply(Admin, session, factors, enforce: switchedOn);

        (await Policy(switchedOn, passkeysRun: true).EvaluateAsync(UserId, null, Admin, session, CancellationToken.None))
            .Should().BeEquivalentTo(today, because);
        (await Policy(switchedOn, passkeysRun: false).EvaluateAsync(UserId, null, Admin, session, CancellationToken.None))
            .Should().BeEquivalentTo(today, because);
    }

    public static TheoryData<string, QualifyingSecondFactors, bool, AuthenticationMethods, ApplicationMfaRequirement> ApplicationMatrix => new()
    {
        // Because, the inventory, whether passkeys run, the session, the answer of an application
        // that requires two steps.
        { "passkey-only, password session", PasskeyOnly, true, PasswordSession, ApplicationMfaRequirement.Passkey },
        { "passkey-only, passkey session", PasskeyOnly, true, PasskeySession, ApplicationMfaRequirement.None },
        { "passkey-only while passkeys do not run: no usable factor", PasskeyOnly, false, PasswordSession, ApplicationMfaRequirement.Enroll },
        { "app and passkey, password session", Both, true, PasswordSession, ApplicationMfaRequirement.StepUp },
        { "app and passkey while passkeys do not run: the app", Both, false, PasswordSession, ApplicationMfaRequirement.StepUp },
        { "app and passkey, passkey session", Both, true, PasskeySession, ApplicationMfaRequirement.None },
        { "app alone, password session", AppOnly, true, PasswordSession, ApplicationMfaRequirement.StepUp },
        { "no factor", QualifyingSecondFactors.None, true, PasswordSession, ApplicationMfaRequirement.Enroll },
    };

    [Theory]
    [MemberData(nameof(ApplicationMatrix))]
    public async Task ApplicationRequirement_WeighsTheUsableFactors(
        string because, QualifyingSecondFactors inventory, bool passkeysRun, AuthenticationMethods session,
        ApplicationMfaRequirement expected)
    {
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(inventory);
        var policy = new ApplicationMfaPolicy(TestHelpers.UsableFactors(_store.Object, passkeysRun));

        (await policy.EvaluateAsync(requireTwoFactor: true, UserId, session, CancellationToken.None)).Should().Be(expected, because);
        (await policy.EvaluateAsync(requireTwoFactor: false, UserId, session, CancellationToken.None))
            .Should().Be(ApplicationMfaRequirement.None, $"{because}: an application that does not require two steps asks nothing");
    }

    private PlatformMfaPolicy Policy(bool switchedOn, bool passkeysRun) => new(
        TestHelpers.UsableFactors(_store.Object, passkeysRun),
        Mock.Of<ITokenClaimsResolver>(),
        TestHelpers.CreateOptions(new TwoFactorSettings { EnforceForPlatformAdmins = switchedOn }),
        TestHelpers.LoadedSettingsReloader(),
        new EnforcedSettingsWarning(),
        _log);
}
