using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (M7): the two MFA policies name the passkey. An account whose one qualifying factor is
/// a passkey can prove two factors only with a passkey sign-in, so a platform administrator
/// gets <c>MfaRequirement.Passkey</c> and an application that requires two-step verification
/// gets <c>ApplicationMfaRequirement.Passkey</c>. Every account without a passkey gets exactly
/// the answer it got before (stop condition 4).
/// </summary>
/// <remarks>Deliberate break, verified red: the passkey-only branch removed (StepUp returned).</remarks>
public class PasskeyMfaPolicyTests
{
    private static readonly QualifyingSecondFactors None = QualifyingSecondFactors.None;
    private static readonly QualifyingSecondFactors AppOnly = new(Totp: true, Passkey: false);
    private static readonly QualifyingSecondFactors PasskeyOnly = new(Totp: false, Passkey: true);
    private static readonly QualifyingSecondFactors Both = new(Totp: true, Passkey: true);

    private static readonly AuthenticationMethods PasskeySignIn =
        AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);

    private static readonly TokenClaims Administrator = new([], ["users.read"], []);

    public static TheoryData<string, QualifyingSecondFactors, AuthenticationMethods, MfaRequirement> PlatformCases => new()
    {
        // The passkey-only account: only a passkey sign-in carries the authority.
        { "passkey-only, password", PasskeyOnly, AuthenticationMethods.Password, MfaRequirement.Passkey },
        { "passkey-only, passwordless emailed code", PasskeyOnly, AuthenticationMethods.EmailCode.With(AuthenticationMethods.EmailCodeFirstFactor), MfaRequirement.Passkey },
        { "passkey-only, unknown", PasskeyOnly, AuthenticationMethods.Unknown, MfaRequirement.Passkey },
        { "passkey-only, passkey sign-in", PasskeyOnly, PasskeySignIn, MfaRequirement.None },
        // Today's answers, unchanged.
        { "no factor, password", None, AuthenticationMethods.Password, MfaRequirement.Enroll },
        { "no factor, unknown", None, AuthenticationMethods.Unknown, MfaRequirement.Enroll },
        { "app, password", AppOnly, AuthenticationMethods.Password, MfaRequirement.StepUp },
        { "app, password and code", AppOnly, AuthenticationMethods.Password.With(AuthenticationMethods.Totp), MfaRequirement.None },
        { "app, emailed code of a password account", AppOnly, AuthenticationMethods.EmailCode, MfaRequirement.Reauthenticate },
        { "app, unknown", AppOnly, AuthenticationMethods.Unknown, MfaRequirement.Reauthenticate },
        // Both: the app answers the step-up as it always has.
        { "app and passkey, password", Both, AuthenticationMethods.Password, MfaRequirement.StepUp },
        { "app and passkey, passkey sign-in", Both, PasskeySignIn, MfaRequirement.None },
    };

    [Theory]
    [MemberData(nameof(PlatformCases))]
    public void PlatformPolicy_NamesThePasskey_OnlyForAPasskeyOnlyAccount(
        string because, QualifyingSecondFactors factors, AuthenticationMethods methods, MfaRequirement expected)
    {
        var decision = TestHelpers.CreatePlatformMfaPolicy(enforce: true).Apply(Administrator, methods, factors, enforce: true);

        decision.Assessed.Should().Be(expected, because);
        decision.Requirement.Should().Be(expected, because);
        decision.Withheld.Should().Be(expected != MfaRequirement.None, because);
    }

    [Fact]
    public void PlatformPolicy_WithNoPlatformAuthority_AsksNothing()
    {
        var decision = TestHelpers.CreatePlatformMfaPolicy(enforce: true)
            .Apply(new TokenClaims([], [], []), AuthenticationMethods.Password, PasskeyOnly, enforce: true);

        decision.Assessed.Should().Be(MfaRequirement.None);
    }

    [Fact]
    public void PlatformPolicy_SwitchedOff_OnlyReportsThePasskeyRequirement()
    {
        var decision = TestHelpers.CreatePlatformMfaPolicy().Apply(Administrator, AuthenticationMethods.Password, PasskeyOnly, enforce: false);

        decision.Assessed.Should().Be(MfaRequirement.Passkey);
        decision.Requirement.Should().Be(MfaRequirement.None);
        decision.Withheld.Should().BeFalse();
    }

    public static TheoryData<string, QualifyingSecondFactors, AuthenticationMethods, ApplicationMfaRequirement> ApplicationCases => new()
    {
        { "passkey-only, password", PasskeyOnly, AuthenticationMethods.Password, ApplicationMfaRequirement.Passkey },
        { "passkey-only, unknown", PasskeyOnly, AuthenticationMethods.Unknown, ApplicationMfaRequirement.Passkey },
        { "passkey-only, passkey sign-in", PasskeyOnly, PasskeySignIn, ApplicationMfaRequirement.None },
        { "no factor, password", None, AuthenticationMethods.Password, ApplicationMfaRequirement.Enroll },
        { "app, password", AppOnly, AuthenticationMethods.Password, ApplicationMfaRequirement.StepUp },
        { "app, password and code", AppOnly, AuthenticationMethods.Password.With(AuthenticationMethods.Totp), ApplicationMfaRequirement.None },
        { "app, emailed code of a password account", AppOnly, AuthenticationMethods.EmailCode, ApplicationMfaRequirement.Reauthenticate },
        { "app and passkey, password", Both, AuthenticationMethods.Password, ApplicationMfaRequirement.StepUp },
    };

    [Theory]
    [MemberData(nameof(ApplicationCases))]
    public void ApplicationPolicy_NamesThePasskey_OnlyForAPasskeyOnlyAccount(
        string because, QualifyingSecondFactors factors, AuthenticationMethods methods, ApplicationMfaRequirement expected)
    {
        var policy = new ApplicationMfaPolicy(Mock.Of<ITwoFactorStateStore>(MockBehavior.Strict));

        policy.Evaluate(requireTwoFactor: true, methods, factors).Should().Be(expected, because);
        policy.Evaluate(requireTwoFactor: false, methods, factors).Should().Be(ApplicationMfaRequirement.None);
    }

    [Fact]
    public void BothCaseTables_AreNotEmpty()
    {
        PlatformCases.Count.Should().BeGreaterThan(0);
        ApplicationCases.Count.Should().BeGreaterThan(0);
    }
}
