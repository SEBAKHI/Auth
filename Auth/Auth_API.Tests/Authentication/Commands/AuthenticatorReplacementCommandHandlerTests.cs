using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.BeginAuthenticatorReplacement;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.ConfirmAuthenticatorReplacement;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// S08 T8: moving the factor to a new authenticator. Starting proves the CURRENT
/// factor from a recent two-factor session and stores the new secret beside it;
/// confirming checks a code from the NEW app against the waiting secret — through
/// the same verifier — and swaps it in with A3f, which also records the new
/// secret's step. The real guard and verifier run under the handlers.
/// </summary>
public class AuthenticatorReplacementCommandHandlerTests
{
    private const string ProtectedSecret = "v2:current-secret";
    private const string PlainSecret = "CURRENTSECRET";
    private const string ProtectedPending = "v2:pending-secret";
    private const string PlainPending = "NEWSECRET";
    private const long CurrentStep = 59_313_872;
    private const long NewAppStep = 59_313_890;
    private const string StoredCodes = "[\"hash-1\",\"hash-2\"]";

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPlatformSettingsRepository> _platform = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<ILogger<BeginAuthenticatorReplacementCommandHandler>> _beginLogger = new();
    private readonly TwoFactorSettings _settings = new();
    private readonly List<IDomainEvent> _dispatched = [];
    private readonly ReauthenticationGuard _guard;
    private readonly SecondFactorVerifier _verifier;

    public AuthenticatorReplacementCommandHandlerTests()
    {
        _verifier = new SecondFactorVerifier(
            _store.Object,
            [
                new TotpProofStrategy(_totp.Object, _protector.Object),
                new RecoveryCodeProofStrategy(_totp.Object)
            ]);
        _guard = new ReauthenticationGuard(
            _sessions.Object,
            TestHelpers.CreateOptions(_settings),
            new FixedTimeProvider(new DateTimeOffset(Now)),
            Mock.Of<ILogger<ReauthenticationGuard>>());

        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: SessionId, userId: UserId, createdAt: Now.AddMinutes(-2), deviceName: "Edge on Windows",
                methods: AuthenticationMethods.Password.With(AuthenticationMethods.Totp)));
        _users.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: UserId, email: "owner@example.org", twoFactorEnabled: true));
        _dispatcher
            .Setup(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Callback<AggregateRoot, CancellationToken>((root, _) => _dispatched.AddRange(root.DomainEvents))
            .Returns(Task.CompletedTask);

        _store.Setup(s => s.TryReserveAttemptAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _protector.Setup(p => p.UnprotectAsync(UserId, ProtectedSecret, It.IsAny<CancellationToken>())).ReturnsAsync(PlainSecret);
        _protector.Setup(p => p.UnprotectAsync(UserId, ProtectedPending, It.IsAny<CancellationToken>())).ReturnsAsync(PlainPending);
        _protector.Setup(p => p.ProtectAsync(UserId, "GENERATED", It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedPending);
        _totp.Setup(t => t.ValidateCode(PlainSecret, "111111")).Returns(CurrentStep);
        _totp.Setup(t => t.ValidateCode(PlainPending, "222222")).Returns(NewAppStep);
        _totp.Setup(t => t.GenerateSecret()).Returns("GENERATED");
        _totp.Setup(t => t.GenerateQrCodeUri("GENERATED", It.IsAny<string>(), It.IsAny<string>())).Returns("otpauth://totp/x");
        _totp.Setup(t => t.GenerateRecoveryCodes(It.IsAny<int>())).Returns(["NEW-1"]);
        _totp.Setup(t => t.HashRecoveryCode(It.IsAny<string>())).Returns<string>(code => $"h({code})");
    }

    private BeginAuthenticatorReplacementCommandHandler Begin() =>
        new(
            _guard,
            _verifier,
            _store.Object,
            _protector.Object,
            new AuthenticatorKeyFactory(
                _platform.Object,
                _totp.Object,
                TestHelpers.CreateOptions(new JwtSettings { Issuer = "https://auth.example.com" }),
                Mock.Of<ILogger<AuthenticatorKeyFactory>>()),
            new TotpReplayPolicy(TestHelpers.CreateOptions(_settings)),
            _users.Object,
            _beginLogger.Object);

    private ConfirmAuthenticatorReplacementCommandHandler Confirm() =>
        new(
            _guard,
            _verifier,
            _store.Object,
            _totp.Object,
            _users.Object,
            _revocation.Object,
            _dispatcher.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)),
            Mock.Of<ILogger<ConfirmAuthenticatorReplacementCommandHandler>>());

    private static ConfirmAuthenticatorReplacementCommand ConfirmWith(string code) =>
        new(UserId, code, SessionId, "sso-cookie-of-this-browser", null);

    private void VerifyNobodySignedOut() =>
        _revocation.Verify(r => r.RevokeCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void GivenFactor(string? pending = null, DateTime? pendingCreatedAt = null) =>
        _store.Setup(s => s.GetSnapshotAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                UserId, ProtectedSecret, StoredCodes, isEnabled: true, failedAttempts: 0, lockedUntil: null,
                pendingSecretKey: pending, pendingSecretCreatedAt: pendingCreatedAt));

    private void GivenConfirm(LoginCommitOutcome outcome) =>
        _store.Setup(s => s.TryConfirmReplacementAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    // ── Starting ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Begin_ProvesTheCurrentFactor_ThenStoresTheNewSecret_AndReturnsIt()
    {
        GivenFactor();
        _store.Setup(s => s.TryBeginReplacementAsync(
                It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Begin().Handle(new BeginAuthenticatorReplacementCommand(UserId, "111111", false, SessionId, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Secret.Should().Be("GENERATED");
        result.Value.EmailCodeRequired.Should().BeFalse("the emailed code belongs to a first factor only");
        _store.Verify(s => s.TryBeginReplacementAsync(
            UserId,
            It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.Totp && p.Step == CurrentStep),
            true,
            ProtectedPending,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Begin_ReusedCodeWithTheSwitchOff_SucceedsAndLogsTheReuse()
    {
        // Window W of X01: with RejectReusedCodes off a reused code settles, and the
        // line an operator reviews is written here too.
        _settings.RejectReusedCodes = false;
        GivenFactor();
        _store.Setup(s => s.TryBeginReplacementAsync(
                It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.ReuseAccepted);

        var result = await Begin().Handle(new BeginAuthenticatorReplacementCommand(UserId, "111111", false, SessionId, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Secret.Should().Be("GENERATED");
        _store.Verify(s => s.TryBeginReplacementAsync(
            UserId, It.IsAny<SecondFactorProof>(), false, ProtectedPending, It.IsAny<CancellationToken>()), Times.Once);
        _beginLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Reused two-factor code accepted")
                    && v.ToString()!.Contains("replace-authenticator") && !v.ToString()!.Contains("111111")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Begin_FromASessionThatProvedOnlyThePassword_AsksToSignInAgain()
    {
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: SessionId, userId: UserId, createdAt: Now.AddMinutes(-2), methods: AuthenticationMethods.Password));
        GivenFactor();

        var result = await Begin().Handle(new BeginAuthenticatorReplacementCommand(UserId, "111111", false, SessionId, null), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Confirming ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_WithNothingWaiting_IsRefused_BeforeAnythingIsCounted()
    {
        GivenFactor(pending: null);

        var result = await Confirm().Handle(ConfirmWith("222222"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.NoPendingReplacement.Code);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_WithAnExpiredReplacement_IsRefused_BeforeAnythingIsCounted()
    {
        GivenFactor(ProtectedPending, Now.AddMinutes(-TwoFactorAuth.PendingReplacementLifetimeMinutes).AddSeconds(-1));

        var result = await Confirm().Handle(ConfirmWith("222222"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.NoPendingReplacement.Code);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryConfirmReplacementAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_ACodeFromTheOldApp_IsWrong_AndCounted()
    {
        GivenFactor(ProtectedPending, Now.AddMinutes(-3));

        var result = await Confirm().Handle(ConfirmWith("111111"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code,
            "the code is checked against the WAITING secret: the old app's code proves nothing about the new one");
        _store.Verify(s => s.TryReserveAttemptAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.TryConfirmReplacementAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_SwapsInTheSecretAsRead_WithTheNewAppsStep_AndTellsTheOwner()
    {
        GivenFactor(ProtectedPending, Now.AddMinutes(-3));
        GivenConfirm(LoginCommitOutcome.Committed);

        var result = await Confirm().Handle(ConfirmWith("222222"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().Equal("NEW-1");
        // A3f: the waiting secret exactly as read, and the step the NEW app's code
        // matched; A3g: the recovery codes as this request read them (row 104 (b)).
        _store.Verify(s => s.TryConfirmReplacementAsync(
            UserId, ProtectedPending, NewAppStep, StoredCodes, "[\"h(NEW-1)\"]", It.IsAny<CancellationToken>()), Times.Once);
        _dispatched.Should().ContainSingle().Which.Should().BeOfType<TwoFactorAuthenticatorReplacedEvent>()
            .Which.DeviceName.Should().Be("Edge on Windows");
    }

    [Theory]
    [InlineData(LoginCommitOutcome.ChallengeLost, "TwoFactor.NoPendingReplacement")]
    [InlineData(LoginCommitOutcome.FactorLost, "User.TwoFactorNotEnabled")]
    public async Task Confirm_SwapRefused_ShowsNoCodes_AndTellsNobody(LoginCommitOutcome outcome, string code)
    {
        GivenFactor(ProtectedPending, Now.AddMinutes(-3));
        GivenConfirm(outcome);

        var result = await Confirm().Handle(ConfirmWith("222222"), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _dispatched.Should().BeEmpty();
        VerifyNobodySignedOut();
    }

    [Fact]
    public async Task Confirm_SignsOutEveryOtherSession_KeepingThisOneAndItsCookie()
    {
        // Row 103 (c): whoever signed in with the old app must not stay signed in.
        GivenFactor(ProtectedPending, Now.AddMinutes(-3));
        GivenConfirm(LoginCommitOutcome.Committed);

        var result = await Confirm().Handle(ConfirmWith("222222"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _revocation.Verify(r => r.RevokeCredentialsAsync(
            UserId, SessionId, "sso-cookie-of-this-browser", UserId, "Authenticator replaced", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Confirm_AFailedSignOut_StillShowsTheCodesAndTellsTheOwner()
    {
        GivenFactor(ProtectedPending, Now.AddMinutes(-3));
        GivenConfirm(LoginCommitOutcome.Committed);
        _revocation.Setup(r => r.RevokeCredentialsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the database went away"));

        var result = await Confirm().Handle(ConfirmWith("222222"), CancellationToken.None);

        result.IsError.Should().BeFalse("the secret is already replaced: its codes must be shown");
        _dispatched.Should().ContainSingle().Which.Should().BeOfType<TwoFactorAuthenticatorReplacedEvent>();
    }

    [Fact]
    public async Task Confirm_ACodeFromTheOldApp_SignsNobodyOut()
    {
        GivenFactor(ProtectedPending, Now.AddMinutes(-3));

        await Confirm().Handle(ConfirmWith("111111"), CancellationToken.None);

        VerifyNobodySignedOut();
    }
}
