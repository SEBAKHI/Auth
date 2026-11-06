using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (FA11 (11-2), OI-48 (2)): beside a live passkey the authenticator app is a further
/// factor, so a session that proved one factor can neither set it up nor switch it on, and no
/// emailed code is asked: the two-factor session is the proof. That holds while passkeys run;
/// while they do not (S20 PR B, D-150-4 «off means off») the passkey is no usable factor, so
/// the app is the account's first one, set up and switched on as before passkeys, with the
/// emailed code X02 asks of a first factor. An account without a passkey answers as it always
/// has, either way.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the FA2 line dropped from setup; enable's email-code
/// decision no longer gated on FA2; the sender's FA2 check dropped. PR B: setup reading the
/// inventory instead of the usable factors (the off-means-off rows turn red).
/// </remarks>
public class TotpEnableRequiresMfaWhenPasskeyBoundTests
{
    private const string ProtectedSecret = "v2:pending-secret";
    private const string PlainSecret = "TESTSECRET";
    private const long MatchedStep = 59_313_872;

    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly QualifyingSecondFactors PasskeyOnly = new(Totp: false, Passkey: true);
    private static readonly AuthenticationMethods TwoFactorSession =
        AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IReauthenticationGuard> _guard = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<ITwoFactorBindCodeRepository> _bindCodes = new(MockBehavior.Strict);
    private readonly EmailSettings _email = new() { Enabled = true };

    // Passkeys run, as on a deployment that switched them on: PR A's answers. The rows that
    // switch them off are «off means off» (D-150-4).
    private bool _passkeysRun = true;

    public TotpEnableRequiresMfaWhenPasskeyBoundTests()
    {
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId, email: "owner@example.com"));
        _totp.Setup(t => t.GenerateSecret()).Returns("ABCDEFGHIJKLMNOP");
        _protector.Setup(p => p.ProtectAsync(_userId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedSecret);
        _store.Setup(s => s.TryStorePendingSecretAsync(_userId, ProtectedSecret, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    // ── Setup ──

    [Fact]
    public async Task Setup_BesideAPasskey_FromAPasswordSession_IsAskedToSignInWithTwoFactors()
    {
        GivenSession(AuthenticationMethods.Password);
        GivenFactors(PasskeyOnly);

        var result = await Setup().Handle(new SetupTwoFactorCommand(_userId, SessionId), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _store.Verify(s => s.TryStorePendingSecretAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "no secret is generated or stored");
    }

    [Fact]
    public async Task Setup_BesideAPasskey_FromATwoFactorSession_GoesOn_AndAsksNoEmailedCode()
    {
        GivenSession(TwoFactorSession);
        GivenFactors(PasskeyOnly);

        var result = await Setup().Handle(new SetupTwoFactorCommand(_userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.EmailCodeRequired.Should().BeFalse("binding a further factor needs the two-factor session, not the emailed code");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Setup_WithoutAPasskey_AnswersAsBefore(bool emailOn)
    {
        _email.Enabled = emailOn;
        GivenSession(AuthenticationMethods.Password);
        GivenFactors(QualifyingSecondFactors.None);

        var result = await Setup().Handle(new SetupTwoFactorCommand(_userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.EmailCodeRequired.Should().Be(emailOn);
    }

    // ── Enable ──

    [Fact]
    public async Task Enable_BesideAPasskey_FromAPasswordSession_IsRefused_BeforeAnAttemptIsCounted()
    {
        GivenPendingFactor(AuthenticationMethods.Password);
        GivenFactors(PasskeyOnly);

        var result = await Enable().Handle(new EnableTwoFactorCommand(_userId, "123456", SessionId, "203.0.113.7"), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enable_BesideAPasskey_FromATwoFactorSession_BindsWithoutAnEmailedCode()
    {
        // Email is on; the strict bind-code mock fails the test if the code is asked for.
        GivenPendingFactor(TwoFactorSession);
        GivenFactors(PasskeyOnly);
        _store.Setup(s => s.TryEnableAsync(
                _userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<Guid?>(), It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Enable().Handle(new EnableTwoFactorCommand(_userId, "123456", SessionId, "203.0.113.7"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryEnableAsync(
            _userId, ProtectedSecret, It.IsAny<string>(), MatchedStep, null, It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enable_WithoutAPasskey_EmailOn_StillAsksForTheEmailedCode()
    {
        // Today's answer for a first factor (X02), unchanged.
        GivenPendingFactor(AuthenticationMethods.Password);
        GivenFactors(QualifyingSecondFactors.None);

        var result = await Enable().Handle(new EnableTwoFactorCommand(_userId, "123456", SessionId, "203.0.113.7"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeRequired.Code);
    }

    // ── The sender ──

    [Fact]
    public async Task SendEmailCode_BesideAPasskey_MintsNoCode()
    {
        GivenSession(TwoFactorSession);
        _store.Setup(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(_userId, ProtectedSecret, null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        GivenFactors(PasskeyOnly);

        var result = await Sender().Handle(new SendTwoFactorEmailCodeCommand(_userId, SessionId, "203.0.113.7"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.EmailCodeRequired.Should().BeFalse("the strict bind-code mock proves nothing was minted");
    }

    // ── Off means off (S20 PR B, D-150-4) ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Setup_BesideAPasskey_WhilePasskeysDoNotRun_GoesOnFromAPasswordSession_AsAFirstFactor(bool emailOn)
    {
        _passkeysRun = false;
        _email.Enabled = emailOn;
        GivenSession(AuthenticationMethods.Password);
        GivenFactors(PasskeyOnly);

        var result = await Setup().Handle(new SetupTwoFactorCommand(_userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeFalse("no two-factor session is asked: the passkey is no usable factor");
        result.Value.EmailCodeRequired.Should().Be(emailOn, "the emailed code of X02 applies to a first factor");
    }

    [Fact]
    public async Task Enable_BesideAPasskey_WhilePasskeysDoNotRun_AsksTheEmailedCode_AsForAFirstFactor()
    {
        _passkeysRun = false;
        GivenPendingFactor(AuthenticationMethods.Password);
        GivenFactors(PasskeyOnly);

        var result = await Enable().Handle(new EnableTwoFactorCommand(_userId, "123456", SessionId, "203.0.113.7"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeRequired.Code,
            "a password session may bind it, and the mailbox proof of a first factor is asked");
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendEmailCode_BesideAPasskey_WhilePasskeysDoNotRun_MintsTheCode()
    {
        _passkeysRun = false;
        GivenSession(AuthenticationMethods.Password);
        _store.Setup(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(_userId, ProtectedSecret, null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        GivenFactors(PasskeyOnly);
        var bindCodes = new Mock<ITwoFactorBindCodeRepository>();
        var notifications = new Mock<INotificationService>();
        notifications.Setup(n => n.SendAsync(It.IsAny<Auth.Application.Notifications.NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr.ErrorOr<ErrorOr.Success>)ErrorOr.Result.Success);
        var otpGenerator = new Mock<IOtpGenerator>();
        otpGenerator.Setup(g => g.GenerateNumericOtp(It.IsAny<int>())).Returns("654321");
        var proof = new FirstFactorEmailProof(
            bindCodes.Object, _users.Object, notifications.Object, otpGenerator.Object,
            Mock.Of<IOtpHasher>(hasher => hasher.Hash(It.IsAny<string>(), It.IsAny<string>()) == "fresh-hash"),
            TestHelpers.CreateOptions(_email), TimeProvider.System, NullLogger<FirstFactorEmailProof>.Instance);

        var result = await new SendTwoFactorEmailCodeCommandHandler(
                _guard.Object, new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(_email)), _store.Object,
                TestHelpers.UsableFactors(_store.Object, passkeysRun: false), proof)
            .Handle(new SendTwoFactorEmailCodeCommand(_userId, SessionId, "203.0.113.7"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.EmailCodeRequired.Should().BeTrue("a first factor gets its mailbox proof");
        notifications.Verify(n => n.SendAsync(It.IsAny<Auth.Application.Notifications.NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void EachOfTheThree_ReadsTheUsableFactors_NeverTheSwitchItself()
    {
        Type[] handlers = [typeof(SetupTwoFactorCommandHandler), typeof(EnableTwoFactorCommandHandler), typeof(SendTwoFactorEmailCodeCommandHandler)];
        foreach (var handler in handlers)
        {
            var parameters = handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .ToList();

            parameters.Should().Contain(typeof(UsableSecondFactors),
                $"{handler.Name} asks whether a passkey is usable in the one place that decides it (T-B6)");
            parameters.Should().NotContain(typeof(PasskeyFeature))
                .And.NotContain(typeof(IOptionsMonitor<PasskeySettings>))
                .And.NotContain(typeof(IOptionsSnapshot<PasskeySettings>))
                .And.NotContain(typeof(IOptions<PasskeySettings>));
        }
    }

    // ── Fixture ──

    private void GivenSession(AuthenticationMethods methods) =>
        _guard.Setup(g => g.EnsureRecentSignInAsync(_userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, "Firefox on Linux", methods));

    private void GivenFactors(QualifyingSecondFactors factors) =>
        _store.Setup(s => s.GetQualifyingFactorsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(factors);

    private void GivenPendingFactor(AuthenticationMethods methods)
    {
        GivenSession(methods);
        _store.Setup(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(_userId, ProtectedSecret, recoveryCodes: null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        _store.Setup(s => s.TryReserveAttemptAsync(_userId, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _protector.Setup(p => p.UnprotectAsync(_userId, ProtectedSecret, It.IsAny<CancellationToken>())).ReturnsAsync(PlainSecret);
        _totp.Setup(t => t.ValidateCode(PlainSecret, "123456")).Returns(MatchedStep);
        _totp.Setup(t => t.GenerateRecoveryCodes(10)).Returns(["AAAA-1111", "BBBB-2222"]);
        _totp.Setup(t => t.HashRecoveryCode(It.IsAny<string>())).Returns<string>(code => $"hash:{code}");
    }

    private FirstFactorEmailProof EmailProof() =>
        new(_bindCodes.Object, _users.Object, Mock.Of<INotificationService>(MockBehavior.Strict),
            Mock.Of<IOtpGenerator>(MockBehavior.Strict), Mock.Of<IOtpHasher>(MockBehavior.Strict),
            TestHelpers.CreateOptions(_email), TimeProvider.System, NullLogger<FirstFactorEmailProof>.Instance);

    private SetupTwoFactorCommandHandler Setup() =>
        new(
            _guard.Object,
            _users.Object,
            _store.Object,
            TestHelpers.UsableFactors(_store.Object, _passkeysRun),
            _protector.Object,
            new AuthenticatorKeyFactory(
                Mock.Of<IPlatformSettingsRepository>(),
                _totp.Object,
                TestHelpers.CreateOptions(new JwtSettings { Issuer = "TestIssuer" }),
                Mock.Of<ILogger<AuthenticatorKeyFactory>>()),
            new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(_email)),
            TestHelpers.CreateSecondFactorLockout(),
            NullLogger<SetupTwoFactorCommandHandler>.Instance);

    private EnableTwoFactorCommandHandler Enable() =>
        new(
            _guard.Object,
            new SecondFactorVerifier(
                _store.Object,
                [new TotpProofStrategy(_totp.Object, _protector.Object), new RecoveryCodeProofStrategy(_totp.Object)],
                TestHelpers.CreateSecondFactorLockout()),
            _store.Object,
            TestHelpers.UsableFactors(_store.Object, _passkeysRun),
            _totp.Object,
            new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(_email)),
            EmailProof(),
            _users.Object,
            new FakeCredentialHasher(),
            Mock.Of<IDomainEventDispatcher>(),
            TestHelpers.CreateUnusedEmailCodeFactorProof(),
            Mock.Of<ICredentialRevocationService>(MockBehavior.Strict),
            NullLogger<EnableTwoFactorCommandHandler>.Instance);

    private SendTwoFactorEmailCodeCommandHandler Sender() =>
        new(_guard.Object, new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(_email)), _store.Object,
            TestHelpers.UsableFactors(_store.Object, _passkeysRun), EmailProof());
}
