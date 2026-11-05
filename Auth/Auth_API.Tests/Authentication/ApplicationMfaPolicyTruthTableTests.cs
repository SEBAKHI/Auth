using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S23, AM2: what an application that requires two-step verification asks of a session,
/// row by row of the card's table, plus the three rows OI-215 added (a sign-in by emailed
/// code). The order is the platform policy's: a missing factor is decided before what the
/// session proved.
/// </summary>
/// <remarks>
/// Deliberate break: count the emailed code as a first factor for an account that has a
/// password (<c>PrimaryBits</c> with <c>EmailCodeBit</c> in <see cref="AuthenticationMethods"/>);
/// the row "password account, signed in by code, authenticator on the account" turns red.
/// </remarks>
public class ApplicationMfaPolicyTruthTableTests
{
    private static readonly AuthenticationMethods Password = AuthenticationMethods.Password;
    private static readonly AuthenticationMethods External = AuthenticationMethods.ExternalIdentity;
    private static readonly AuthenticationMethods Totp = AuthenticationMethods.Totp;
    private static readonly AuthenticationMethods Recovery = AuthenticationMethods.RecoveryCode;

    // A sign-in by emailed code (OI-215): an account with a password records the code alone,
    // an account without one records the code as its first factor.
    private static readonly AuthenticationMethods CodeOfPasswordAccount = AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: true);
    private static readonly AuthenticationMethods CodeOfPasswordlessAccount = AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false);

    private readonly ApplicationMfaPolicy _policy = new(new Mock<ITwoFactorStateStore>(MockBehavior.Strict).Object);

    public static TheoryData<string, bool, AuthenticationMethods, bool, ApplicationMfaRequirement> Rows => new()
    {
        // The flag off: nothing more, whatever was proved.
        { "flag off, nothing recorded", false, AuthenticationMethods.Unknown, false, ApplicationMfaRequirement.None },
        { "flag off, password only", false, Password, true, ApplicationMfaRequirement.None },
        // Two factors proved.
        { "password and authenticator code", true, Password.With(Totp), true, ApplicationMfaRequirement.None },
        { "password and recovery code", true, Password.With(Recovery), true, ApplicationMfaRequirement.None },
        { "provider and authenticator code", true, External.With(Totp), true, ApplicationMfaRequirement.None },
        // A first factor, and the account has an authenticator: sign in with it.
        { "password only, authenticator on the account", true, Password, true, ApplicationMfaRequirement.StepUp },
        { "provider only, authenticator on the account", true, External, true, ApplicationMfaRequirement.StepUp },
        // No qualifying factor on the account: set one up first. An emailed code never qualifies.
        { "password and emailed second step, no authenticator", true, Password.With(AuthenticationMethods.EmailCode), false, ApplicationMfaRequirement.Enroll },
        { "emailed code only, no authenticator", true, CodeOfPasswordAccount, false, ApplicationMfaRequirement.Enroll },
        { "password only, no authenticator", true, Password, false, ApplicationMfaRequirement.Enroll },
        // Nothing recorded (a session from before methods existed).
        { "nothing recorded, authenticator on the account", true, AuthenticationMethods.Unknown, true, ApplicationMfaRequirement.Reauthenticate },
        { "nothing recorded, no authenticator", true, AuthenticationMethods.Unknown, false, ApplicationMfaRequirement.Enroll },
        // Proved two factors, then removed the authenticator: Enroll is decided first (2-2 before 2-3).
        { "password and authenticator code, authenticator removed since", true, Password.With(Totp), false, ApplicationMfaRequirement.Enroll },
        // OI-215 rows.
        { "passwordless account by code, and authenticator code", true, CodeOfPasswordlessAccount.With(Totp), true, ApplicationMfaRequirement.None },
        { "password account by code, authenticator on the account", true, CodeOfPasswordAccount.With(Totp), true, ApplicationMfaRequirement.Reauthenticate },
        { "password account by code only, authenticator on the account", true, CodeOfPasswordAccount, true, ApplicationMfaRequirement.Reauthenticate },
        { "passwordless account by code, authenticator on the account", true, CodeOfPasswordlessAccount, true, ApplicationMfaRequirement.StepUp },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Evaluate_EachRow_GivesItsRequirement(
        string row, bool requireTwoFactor, AuthenticationMethods methods, bool hasFactor, ApplicationMfaRequirement expected)
    {
        _policy.Evaluate(requireTwoFactor, methods, new QualifyingSecondFactors(Totp: hasFactor, Passkey: false)).Should().Be(expected, row);
    }

    [Fact]
    public void Evaluate_WithoutAPasskey_NeverProducesPasskey()
    {
        // S20: Passkey is produced only for an account whose only qualifying factor is a
        // passkey (ApplicationMfaPolicyPasskeyTests); every row here has none, and answers as
        // before passkeys shipped.
        var every = Rows.Select(row => _policy.Evaluate(
            (bool)row[1], (AuthenticationMethods)row[2], new QualifyingSecondFactors(Totp: (bool)row[3], Passkey: false)));

        every.Should().NotContain(ApplicationMfaRequirement.Passkey);
        Rows.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EvaluateAsync_FlagOff_NeverReadsTheAccountsFactor()
    {
        // A Strict mock: any read throws.
        var outcome = await _policy.EvaluateAsync(
            requireTwoFactor: false, Guid.NewGuid(), AuthenticationMethods.Unknown, CancellationToken.None);

        outcome.Should().Be(ApplicationMfaRequirement.None);
    }

    [Fact]
    public async Task EvaluateAsync_FlagOn_ReadsTheEnabledFactorOfThatUser()
    {
        var userId = Guid.NewGuid();
        var store = new Mock<ITwoFactorStateStore>(MockBehavior.Strict);
        store.Setup(s => s.GetQualifyingFactorsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualifyingSecondFactors(Totp: true, Passkey: false));

        var outcome = await new ApplicationMfaPolicy(store.Object).EvaluateAsync(
            requireTwoFactor: true, userId, Password, CancellationToken.None);

        outcome.Should().Be(ApplicationMfaRequirement.StepUp);
        store.Verify(s => s.GetQualifyingFactorsAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 9, true)]
    [InlineData(true, 17, true)]
    [InlineData(true, 4 | 8, false)]
    [InlineData(true, 4 | 8 | 128, true)]
    public void AdmitsSessionThatProved_IsTheFlagOffOrTwoFactors(bool requireTwoFactor, int stored, bool expected)
    {
        // The exchange and refresh rule (AM5, AM6): what the session proved decides, no factor read.
        var application = Auth.Domain.Entities.Application.Create("CRM", "CRM", null, null, Guid.NewGuid());
        application.Update(
            "CRM", null, null, null, null, null, false, requireTwoFactor, false, 60, 5,
            ApplicationAccessMode.Restricted, Guid.NewGuid());

        application.AdmitsSessionThatProved(AuthenticationMethods.FromStored(stored)).Should().Be(expected);
    }
}
