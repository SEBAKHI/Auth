using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Domain;

/// <summary>
/// The one rule of what a signed-in account must prove to prove itself again: exactly what a
/// sign-in of the same account would ask for, never less. A break here is a stolen session and one
/// stolen secret passing where a sign-in would stop them.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the authenticator app dropped after the password; the
/// unconfirmed address's password accepted; the administrator's password accepted; the account flag
/// with nothing to answer it ignored.
/// </remarks>
public class ReauthenticationRequirementTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static TwoFactorSnapshot App() =>
        new(User, "v2:secret", "[\"h\"]", isEnabled: true, failedAttempts: 0, lockedUntil: null);

    private static TwoFactorSnapshot EmailedCode() =>
        new(User, null, "[\"h\"]", isEnabled: false, failedAttempts: 0, lockedUntil: null,
            modifiedAt: DateTime.UtcNow, emailCodeEnabledAt: DateTime.UtcNow.AddDays(-1), emailFailedAttempts: 0);

    private static ReauthenticationRequirement For(
        bool hasPassword = true,
        bool emailConfirmed = true,
        TwoFactorSnapshot? factor = null,
        bool emailOn = true,
        bool passkeyUsable = false,
        bool passkeyOnlyAdministrator = false,
        bool signInGateOn = false) =>
        ReauthenticationRequirement.For(
            hasPassword, emailConfirmed, factor, emailOn, passkeyUsable, passkeyOnlyAdministrator, signInGateOn);

    [Fact]
    public void APasswordAccount_ProvesItsPassword_AndNothingElse()
    {
        var requirement = For();

        requirement.Password.Should().BeTrue();
        requirement.EmailCode.Should().BeFalse("an emailed code proves the mailbox of a password account, never the person");
        requirement.Passkey.Should().BeFalse();
        requirement.SecondStep.Should().Be(ReauthenticationSecondStep.None);
        requirement.SendsEmailCode.Should().BeFalse();
        requirement.CanProveInPlace.Should().BeTrue();
    }

    [Fact]
    public void TheAuthenticatorApp_FollowsThePassword_AsAtASignIn()
    {
        var requirement = For(factor: App());

        requirement.Password.Should().BeTrue();
        requirement.SecondStep.Should().Be(ReauthenticationSecondStep.AuthenticatorApp);
    }

    [Theory]
    [InlineData(true, ReauthenticationSecondStep.EmailCode)]
    [InlineData(false, ReauthenticationSecondStep.RecoveryCodeOnly)]
    public void TheEmailedSecondFactor_FollowsThePassword_WhileEmailIsOn_AndARecoveryCodeOtherwise(
        bool emailOn, ReauthenticationSecondStep expected)
    {
        var requirement = For(factor: EmailedCode(), emailOn: emailOn);

        requirement.SecondStep.Should().Be(expected);
        requirement.SendsEmailCode.Should().Be(emailOn);
    }

    [Fact]
    public void ALivePasskey_IsOfferedBesideThePassword()
    {
        var requirement = For(factor: App(), passkeyUsable: true);

        requirement.Passkey.Should().BeTrue("a passkey the device verified proves two factors alone");
        requirement.Password.Should().BeTrue();
        requirement.SecondStep.Should().Be(ReauthenticationSecondStep.AuthenticatorApp, "a password still needs what follows it");
    }

    [Fact]
    public void AnAccountWithoutAPassword_ProvesItsMailbox_ItsFirstFactor()
    {
        var requirement = For(hasPassword: false);

        requirement.EmailCode.Should().BeTrue();
        requirement.Password.Should().BeFalse();
        requirement.SecondStep.Should().Be(ReauthenticationSecondStep.None);
        requirement.SendsEmailCode.Should().BeTrue();
    }

    [Fact]
    public void AnAccountWithoutAPassword_IsStillAskedForItsAuthenticatorApp()
    {
        For(hasPassword: false, factor: App()).SecondStep.Should().Be(ReauthenticationSecondStep.AuthenticatorApp);
    }

    [Fact]
    public void AnAccountWithoutAPassword_IsAskedForItsMailboxOnce()
    {
        For(hasPassword: false, factor: EmailedCode()).SecondStep.Should().Be(
            ReauthenticationSecondStep.None, "the code just proved the mailbox its second factor would, as a sign-in by code does");
    }

    [Fact]
    public void WithEmailOff_AnAccountWithoutAPassword_HasNothingToProveInPlace()
    {
        var requirement = For(hasPassword: false, emailOn: false);

        requirement.CanProveInPlace.Should().BeFalse("only an external provider signs it in, and that is a sign-in");
        requirement.SendsEmailCode.Should().BeFalse();
    }

    [Fact]
    public void WithEmailOff_ItsPasskeyStillProvesIt()
    {
        var requirement = For(hasPassword: false, emailOn: false, passkeyUsable: true);

        requirement.CanProveInPlace.Should().BeTrue();
        requirement.Passkey.Should().BeTrue();
        requirement.EmailCode.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAddressNobodyProved_MakesNoPasswordOrCodeAProof(bool hasPassword)
    {
        var requirement = For(hasPassword: hasPassword, emailConfirmed: false);

        requirement.Password.Should().BeFalse("a password set before the address was proved may be a stranger's (D-215-5)");
        requirement.EmailCode.Should().BeFalse("nothing is sent to an address nobody proved");
        requirement.CanProveInPlace.Should().BeFalse();
    }

    [Fact]
    public void APasskeyOnlyAdministrator_ProvesItsPasskey_AndNothingElse()
    {
        var requirement = For(factor: EmailedCode(), passkeyUsable: true, passkeyOnlyAdministrator: true);

        requirement.Passkey.Should().BeTrue();
        requirement.Password.Should().BeFalse("D-S20-4 (ii-s)");
        requirement.EmailCode.Should().BeFalse();
        requirement.SecondStep.Should().Be(ReauthenticationSecondStep.None);
        requirement.SendsEmailCode.Should().BeFalse();
    }

    // Review of the window: the flag sends a sign-in to a second step that nothing answers, so a
    // password alone here would be less than a sign-in asks.
    [Fact]
    public void TheAccountFlag_WithNoFactorToAnswerIt_TakesAPasskey_OrNothing()
    {
        var nothing = For(signInGateOn: true);
        nothing.Password.Should().BeFalse();
        nothing.EmailCode.Should().BeFalse();
        nothing.SecondStep.Should().Be(ReauthenticationSecondStep.None);
        nothing.CanProveInPlace.Should().BeFalse();

        For(signInGateOn: true, hasPassword: false).EmailCode.Should().BeFalse();
        For(signInGateOn: true, passkeyUsable: true).Passkey.Should().BeTrue("it proves both alone, as at a sign-in");
    }

    [Fact]
    public void TheAccountFlag_WithAFactorThatAnswersIt_AsksWhatThatFactorAsks()
    {
        For(signInGateOn: true, factor: App()).SecondStep.Should().Be(ReauthenticationSecondStep.AuthenticatorApp);
        For(signInGateOn: true, factor: EmailedCode()).SecondStep.Should().Be(ReauthenticationSecondStep.EmailCode);
    }
}
