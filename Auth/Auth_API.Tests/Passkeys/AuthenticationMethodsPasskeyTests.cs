using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (T-A1, M3, D-T14 A, D-T18 C): the two passkey bits. <c>Passkey</c> (32) is a first
/// factor; with <c>PasskeyUserVerified</c> (64), the device having verified the person, the two
/// satisfy two factors on their own (64 alone proves nothing, REVIEW 150 F10). Nothing issues
/// them before PR B; these pin what they will mean.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>hwk</c> emitted for the passkey; <c>Passkey</c>
/// dropped from the primaries; <c>PasskeyUserVerified</c> dropped from <c>IsMfaSatisfied</c>;
/// the bare OR of the user-verified bit restored (F10).
/// </remarks>
public class AuthenticationMethodsPasskeyTests
{
    private static readonly AuthenticationMethods PasskeySignIn =
        AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);

    [Fact]
    public void PasskeySignIn_ProvesTwoFactors_AndAFirstFactor_ButNoPassword()
    {
        PasskeySignIn.Value.Should().Be(96);
        PasskeySignIn.IsMfaSatisfied.Should().BeTrue("the device verified the person and holds the key (M3)");
        PasskeySignIn.HasPrimary.Should().BeTrue("a passkey is a first factor (D-T14 A, Fact 18 (a))");
        PasskeySignIn.ProvesPassword.Should().BeFalse("a passkey sign-in never proves the password");
    }

    [Fact]
    public void PasskeyWithoutUserVerification_IsAFirstFactorOnly()
    {
        AuthenticationMethods.Passkey.HasPrimary.Should().BeTrue();
        AuthenticationMethods.Passkey.IsMfaSatisfied.Should().BeFalse("possession alone is one factor");
    }

    /// <summary>
    /// REVIEW 150 F10 (a): the device's check counts only with the passkey it unlocked. The
    /// user-verified bit alone proves no first factor, and beside a password it is no second
    /// one: nothing writes it without the passkey bit (PR B writes both), and a stored value
    /// that did must satisfy no MFA policy.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the bare OR of the user-verified bit restored.</remarks>
    [Fact]
    public void UserVerification_CountsOnlyWithThePasskey()
    {
        AuthenticationMethods.FromStored(64).IsMfaSatisfied.Should().BeFalse("the device's check without the passkey proves nothing");
        AuthenticationMethods.FromStored(96).IsMfaSatisfied.Should().BeTrue("a passkey the device unlocked is two factors (M3)");
        AuthenticationMethods.PasskeyUserVerified.IsMfaSatisfied.Should().BeFalse();
        AuthenticationMethods.Password.With(AuthenticationMethods.PasskeyUserVerified).IsMfaSatisfied.Should().BeFalse(
            "a password and a check of no passkey is one factor");
    }

    [Fact]
    public void PasskeySignIn_Amr_IsPopUserMfa()
    {
        PasskeySignIn.ToAmrValues().Should().Equal("pop", "user", "mfa");
        PasskeySignIn.ToAmrValues().Should().NotContain("hwk", "RFC 8176 hwk is a hardware key; a synced passkey is not one");
        AuthenticationMethods.Passkey.ToAmrValues().Should().Equal("pop");
    }

    [Fact]
    public void TodaysMethods_KeepTodaysAnswers()
    {
        // Stop condition 4: nothing an account without a passkey signs in with changes.
        AuthenticationMethods.Password.ToAmrValues().Should().Equal("pwd");
        AuthenticationMethods.Password.With(AuthenticationMethods.Totp).ToAmrValues().Should().Equal("pwd", "otp", "mfa");
        AuthenticationMethods.Password.With(AuthenticationMethods.RecoveryCode).ToAmrValues().Should().Equal("pwd", "mfa");
        AuthenticationMethods.Password.IsMfaSatisfied.Should().BeFalse();
        AuthenticationMethods.EmailCode.HasPrimary.Should().BeFalse("an emailed code of a password account proves the mailbox only");
        AuthenticationMethods.EmailCode.With(AuthenticationMethods.Totp).IsMfaSatisfied.Should().BeFalse();
        AuthenticationMethods.EmailCodeFirstFactor.HasPrimary.Should().BeTrue();
    }

    [Fact]
    public void PasskeyBits_SurviveTheStoredForm()
    {
        AuthenticationMethods.FromStored(PasskeySignIn.ToStored()).Should().Be(PasskeySignIn);
        AuthenticationMethods.FromStored(32 | 64).IsMfaSatisfied.Should().BeTrue();
    }
}
