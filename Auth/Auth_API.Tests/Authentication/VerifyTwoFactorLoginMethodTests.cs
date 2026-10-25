using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Authentication;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The verify step's <c>method</c> and the login response's <c>twoFactorMethods</c>
/// (S24, EO7): an absent method means what it always meant, "email" completes a
/// password sign-in on the emailed code's own counter, and the challenge offers the
/// factors the account holds.
/// </summary>
public class VerifyTwoFactorLoginMethodTests
{
    private const string Token = "challenge-token";

    private readonly User _user = TestHelpers.CreateUser(twoFactorEnabled: true);
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILoginResponseBuilder> _builder = new();
    private readonly FakeCredentialHasher _keys = new();

    public VerifyTwoFactorLoginMethodTests()
    {
        _keys.Map(Token, "challenge-token-hash");
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _builder
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AuthenticationMethods>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .ReturnsAsync(new LoginResponse());
    }

    private VerifyTwoFactorLoginCommandHandler Handler() => new(
        _challenges.Object,
        new SecondFactorVerifier(
            _store.Object,
            [
                new TotpProofStrategy(Mock.Of<ITotpService>(), Mock.Of<ITwoFactorSecretProtector>()),
                new RecoveryCodeProofStrategy(Mock.Of<ITotpService>()),
                new EmailCodeProofStrategy(ScopedOtpHasher.Instance)
            ],
            TestHelpers.CreateSecondFactorLockout()),
        _store.Object,
        new TotpReplayPolicy(TestHelpers.CreateOptions(new TwoFactorSettings())),
        _users.Object,
        Mock.Of<ILoginAttemptRepository>(),
        _keys,
        _builder.Object,
        Mock.Of<IDomainEventDispatcher>(),
        Mock.Of<ILogger<VerifyTwoFactorLoginCommandHandler>>());

    private TwoFactorChallenge GivenEmailSignIn(AuthenticationMethods primary, params string[] codes)
    {
        var challenge = ChallengeWith(_user.Id, primary, codes: codes);
        _challenges.Setup(c => c.GetByTokenHashAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        _challenges.Setup(c => c.TryReserveAttemptAsync(challenge.Id, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(EmailRow(_user.Id));
        _store.Setup(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _store.Setup(s => s.TryCommitLoginAsync(
                challenge.Id, _user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        return challenge;
    }

    private static VerifyTwoFactorLoginCommand Command(string code, string? method, bool useRecoveryCode = false) =>
        new(Token, code, useRecoveryCode, "203.0.113.7", "agent", Method: method);

    // ── method ──

    [Theory]
    [InlineData(null, false, SecondFactorMethod.Totp)]
    [InlineData(null, true, SecondFactorMethod.RecoveryCode)]
    [InlineData("totp", true, SecondFactorMethod.Totp)]
    [InlineData("recovery", false, SecondFactorMethod.RecoveryCode)]
    [InlineData("email", false, SecondFactorMethod.EmailCode)]
    public void TheMethod_DecidesWhenPresent_AndUseRecoveryCodeWhenAbsent(string? method, bool useRecoveryCode, SecondFactorMethod expected)
    {
        Command("123456", method, useRecoveryCode).PresentedMethod.Should().Be(expected,
            "a client built before the emailed code sends no method and must be read exactly as before");
    }

    /// <summary>
    /// OI-220 (9): the presented method is total. A method that names none reads as
    /// none — never an exception — so the record's own ToString, which a log line or an
    /// assertion message may call before any validator ran, cannot throw; and it never
    /// prints the code or the challenge token.
    /// </summary>
    [Theory]
    [InlineData("Email")]
    [InlineData("sms")]
    [InlineData("")]
    public void AnUnknownMethod_PresentsNone_AndTheCommandStillPrints_WithoutItsSecrets(string method)
    {
        var command = Command("654321", method);

        command.PresentedMethod.Should().BeNull();
        var printed = command.ToString();
        printed.Should().Contain(method.Length == 0 ? "Method = " : method);
        printed.Should().NotContain("654321").And.NotContain(Token);
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("sms")]
    [InlineData("")]
    [InlineData("3")]
    public void AnUnknownMethod_IsRefusedWithItsPublishedCode(string method)
    {
        var result = new VerifyTwoFactorLoginCommandValidator().Validate(Command("123456", method));

        result.Errors.Should().ContainSingle(e => e.ErrorCode == TwoFactorErrors.MethodInvalid.Code);
    }

    [Fact]
    public void AnEmailedCode_IsSixDigits_ARecoveryCode_IsFreeForm()
    {
        var validator = new VerifyTwoFactorLoginCommandValidator();

        validator.Validate(Command("12345", "email")).Errors.Should().ContainSingle(e => e.ErrorCode == TwoFactorErrors.CodeInvalidFormat.Code);
        validator.Validate(Command("ABCD-EFGH", "recovery")).IsValid.Should().BeTrue();
        validator.Validate(Command("123456", "email")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnEmailedCode_CompletesThePasswordSignIn_AsPasswordAndEmailCode_ProvingThePassword()
    {
        GivenEmailSignIn(AuthenticationMethods.Password, "111111");

        var result = await Handler().Handle(Command("111111", "email"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _builder.Verify(b => b.BuildAsync(
            _user, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.Is<AuthenticationMethods>(m => m == AuthenticationMethods.Password.With(AuthenticationMethods.EmailCode) && !m.IsMfaSatisfied),
            true, It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Once,
            "the password was proved, so the automatic lock lifts (G8); the session is not a two-factor one");
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnEmailedCode_CompletesAProviderSignIn_AsExternalIdentityAndEmailCode_ProvingNoPassword()
    {
        // D-243: the code reaches the account's mailbox, which a provider at an
        // outside address may not control.
        GivenEmailSignIn(AuthenticationMethods.ExternalIdentity, "111111");

        var result = await Handler().Handle(Command("111111", "email"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _builder.Verify(b => b.BuildAsync(
            _user, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.Is<AuthenticationMethods>(m =>
                m == AuthenticationMethods.ExternalIdentity.With(AuthenticationMethods.EmailCode)
                && !m.IsMfaSatisfied && m.ToAmrValues().Count == 0),
            false, It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Once,
            "a provider proves no password, so no lock lifts (G8); the session is not a two-factor one");
    }

    [Fact]
    public async Task AnEmailedCode_AfterAVerifyEmailLink_IsRefused_BeforeAnythingIsCounted()
    {
        var challenge = GivenEmailSignIn(AuthenticationMethods.EmailCode, "111111");

        var result = await Handler().Handle(Command("111111", "email"), CancellationToken.None);

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeUnavailable);
        _challenges.Verify(c => c.TryReserveAttemptAsync(challenge.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAbsentMethod_OnAnEmailFactorAccount_IsAnAuthenticatorCode_RefusedAsWrong_NotADeadChallenge()
    {
        GivenEmailSignIn(AuthenticationMethods.Password, "111111");

        var result = await Handler().Handle(Command("111111", method: null), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode,
            "an old client stays on the page and can switch to a recovery code; a dead challenge would send it back to sign in");
    }

    // ── twoFactorMethods ──

    private static TwoFactorChallengeService ChallengeService(TwoFactorSnapshot? row, bool emailEnabled)
    {
        var store = new Mock<ITwoFactorStateStore>();
        store.Setup(s => s.GetSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateRefreshToken()).Returns("token");

        // What is sent is TwoFactorChallengeEmailCodeTests' subject: refused here.
        var sender = new Mock<IEmailCodeSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwoFactorErrors.EmailCodeTooManyRequests);

        return new TwoFactorChallengeService(
            Mock.Of<ITwoFactorChallengeRepository>(),
            Mock.Of<ILoginAttemptRepository>(),
            jwt.Object,
            new FakeCredentialHasher(),
            store.Object,
            TestHelpers.CreateOptions(new EmailSettings { Enabled = emailEnabled }),
            sender.Object,
            TestHelpers.CreateSecondFactorLockout(),
            Mock.Of<ILogger<TwoFactorChallengeService>>());
    }

    public static TheoryData<string, int, bool, string[]?> Offers => new()
    {
        { "totp", 1, true, [TwoFactorMethodNames.Totp, TwoFactorMethodNames.Recovery] },
        { "totp", 2, false, [TwoFactorMethodNames.Totp, TwoFactorMethodNames.Recovery] },
        { "email", 1, true, [TwoFactorMethodNames.Email, TwoFactorMethodNames.Recovery] },
        { "email", 1, false, [TwoFactorMethodNames.Recovery] },
        { "email", 2, true, [TwoFactorMethodNames.Email, TwoFactorMethodNames.Recovery] },
        { "email", 2, false, [TwoFactorMethodNames.Recovery] },
        { "email", 4, true, [TwoFactorMethodNames.Recovery] },
        { "none", 1, true, null },
    };

    [Theory]
    [MemberData(nameof(Offers))]
    public async Task TheChallenge_OffersTheFactorsTheAccountHolds_AndTheEmailedCodeOnlyWhenItCanBeSent(
        string factor, int primary, bool emailEnabled, string[]? expected)
    {
        var user = TestHelpers.CreateUser(twoFactorEnabled: true);
        var row = factor switch
        {
            "totp" => TotpRow(user.Id),
            "email" => EmailRow(user.Id),
            _ => null
        };

        var issued = await ChallengeService(row, emailEnabled).CreateChallengeAsync(
            user, null, null, AuthenticationMethods.FromStored(primary), CancellationToken.None);

        issued.Token.Should().Be("token");
        if (expected is null)
        {
            issued.Methods.Should().BeNull();
        }
        else
        {
            issued.Methods.Should().Equal(expected);
        }
    }

    [Fact]
    public void TheMethodNames_AreTheWireContract()
    {
        TwoFactorMethodNames.All.Should().Equal("totp", "recovery", "email");
        TwoFactorMethodNames.All.Select(TwoFactorMethodNames.MethodOf).Should().Equal(
            SecondFactorMethod.Totp, SecondFactorMethod.RecoveryCode, SecondFactorMethod.EmailCode);
    }
}
