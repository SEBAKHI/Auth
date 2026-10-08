using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Domain;

/// <summary>
/// S08 T6: what a session proved, and which sets count as two factors. An emailed
/// code and an unknown session never do — the first proves a mailbox the
/// password-reset path already hands to whoever holds it, the second proved
/// nothing anyone can read back.
/// </summary>
public class AuthenticationMethodsTests
{
    private static readonly AuthenticationMethods Pwd = AuthenticationMethods.Password;
    private static readonly AuthenticationMethods Ext = AuthenticationMethods.ExternalIdentity;
    private static readonly AuthenticationMethods Email = AuthenticationMethods.EmailCode;
    private static readonly AuthenticationMethods Totp = AuthenticationMethods.Totp;
    private static readonly AuthenticationMethods Recovery = AuthenticationMethods.RecoveryCode;

    public static TheoryData<string, AuthenticationMethods, bool, bool> TruthTable => new()
    {
        // name, set, HasPrimary, IsMfaSatisfied
        { "unknown", AuthenticationMethods.Unknown, false, false },
        { "password", Pwd, true, false },
        { "external identity", Ext, true, false },
        { "email code", Email, false, false },
        { "totp alone", Totp, false, false },
        { "recovery code alone", Recovery, false, false },
        { "password + totp", Pwd.With(Totp), true, true },
        { "password + recovery code", Pwd.With(Recovery), true, true },
        { "external identity + totp", Ext.With(Totp), true, true },
        { "external identity + recovery code", Ext.With(Recovery), true, true },
        { "email code + totp", Email.With(Totp), false, false },
        { "email code + recovery code", Email.With(Recovery), false, false },
        { "password + email code", Pwd.With(Email), true, false },
    };

    [Theory]
    [MemberData(nameof(TruthTable))]
    public void IsMfaSatisfied_FollowsTheTruthTable(string name, AuthenticationMethods methods, bool hasPrimary, bool mfa)
    {
        methods.HasPrimary.Should().Be(hasPrimary, name);
        methods.IsMfaSatisfied.Should().Be(mfa, name);
    }

    [Fact]
    public void EmailCode_IsNeverASecondFactor_NorEmittedAsOtp()
    {
        // The break this test exists for: counting an emailed code as a factor.
        Pwd.With(Email).IsMfaSatisfied.Should().BeFalse("an emailed code proves a mailbox, not a second factor");
        Email.ToAmrValues().Should().BeEmpty("RFC 8176 otp means HOTP/TOTP; an emailed code has no registered value");
        Pwd.With(Email).ToAmrValues().Should().Equal("pwd");
    }

    [Fact]
    public void ToAmrValues_NamesPasswordAndTotp_AndMfaOnlyForTwoFactors()
    {
        Pwd.ToAmrValues().Should().Equal("pwd");
        Pwd.With(Totp).ToAmrValues().Should().Equal("pwd", "otp", "mfa");
        Pwd.With(Recovery).ToAmrValues().Should().Equal("pwd", "mfa");
        Ext.With(Totp).ToAmrValues().Should().Equal("otp", "mfa");
        Ext.ToAmrValues().Should().BeEmpty("an external identity has no registered amr value");
        AuthenticationMethods.Unknown.ToAmrValues().Should().BeEmpty();
    }

    [Fact]
    public void Stored_UnknownIsNull_AndEveryBitRoundTrips()
    {
        AuthenticationMethods.Unknown.ToStored().Should().BeNull("Unknown is stored as NULL");
        AuthenticationMethods.FromStored(null).Should().Be(AuthenticationMethods.Unknown);
        AuthenticationMethods.FromStored(0).IsUnknown.Should().BeTrue();

        // The fixed bits, which a stored row depends on and which never move.
        Pwd.Value.Should().Be(1);
        Ext.Value.Should().Be(2);
        Email.Value.Should().Be(4);
        Totp.Value.Should().Be(8);
        Recovery.Value.Should().Be(16);

        var all = Pwd.With(Ext).With(Email).With(Totp).With(Recovery);
        AuthenticationMethods.FromStored(all.ToStored()).Should().Be(all);
        all.ToStored().Should().Be(31);
    }

    [Fact]
    public void FromStored_IgnoresBitsThisCodeNeverWrites()
    {
        // 32 and 64 are reserved for the passkey methods: until they exist, a value
        // carrying them is read as what it holds of the known bits, and never
        // counts as two factors on their strength.
        AuthenticationMethods.FromStored(1 | 64).Should().Be(Pwd);
        AuthenticationMethods.FromStored(64).IsUnknown.Should().BeTrue();
    }

    [Fact]
    public void With_IsAUnion()
    {
        Pwd.With(Pwd).Should().Be(Pwd);
        Pwd.With(AuthenticationMethods.Unknown).Should().Be(Pwd);
        AuthenticationMethods.Unknown.With(Totp).Should().Be(Totp,
            "a session recorded before methods were gains the factor alone, which proves no first factor");
    }

    [Fact]
    public void From_MapsEverySecondFactorMethod()
    {
        Enum.GetValues<SecondFactorMethod>().Should().HaveCount(2, "a new factor needs its own authentication method");
        AuthenticationMethods.From(SecondFactorMethod.Totp).Should().Be(Totp);
        AuthenticationMethods.From(SecondFactorMethod.RecoveryCode).Should().Be(Recovery);
    }
}
