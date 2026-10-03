using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Binding the FIRST second factor needs the code emailed to the account's
/// confirmed address whenever email is on (X02 PR B; the owner's RV-P8-4 decision):
/// whoever holds only the password cannot bind an authenticator of their own.
/// <para>
/// The real verifier, its TOTP strategy and the real email proof run under the
/// enable handler; storage, TOTP arithmetic, decryption and the keyed hash are
/// stubbed. The code is checked AFTER the authenticator code, under the attempt
/// the pending factor already counted, so a wrong one is a failure of the factor
/// and five lock it. It is never a factor: enable issues no token and touches no
/// session.
/// </para>
/// </summary>
public class FirstFactorEmailProofTests
{
    private const string ProtectedSecret = "v2:pending-secret";
    private const string PlainSecret = "TESTSECRET";
    private const long MatchedStep = 59_313_872;
    private const string TotpCode = "123456";
    private const string EmailCode = "654321";
    private const string StoredHash = "otp:stored-hash";

    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IReauthenticationGuard> _guard = new();
    private readonly Mock<ITwoFactorStateStore> _stateStore = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<ITwoFactorSecretProtector> _secretProtector = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();
    private readonly Mock<ITwoFactorBindCodeRepository> _bindCodes = new(MockBehavior.Strict);
    private readonly Mock<IOtpHasher> _otpHasher = new(MockBehavior.Strict);
    private readonly TwoFactorSettings _twoFactorSettings = new();
    private readonly EmailSettings _emailSettings = new() { Enabled = true };
    private readonly EnableTwoFactorCommandHandler _handler;

    public FirstFactorEmailProofTests()
    {
        var verifier = new SecondFactorVerifier(
            _stateStore.Object,
            [
                new TotpProofStrategy(_totpService.Object, _secretProtector.Object),
                new RecoveryCodeProofStrategy(_totpService.Object)
            ]);

        _handler = new EnableTwoFactorCommandHandler(
            _guard.Object,
            verifier,
            _stateStore.Object,
            _totpService.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_twoFactorSettings)),
            new FirstFactorEmailProofPolicy(
                TestHelpers.CreateOptions(_twoFactorSettings),
                TestHelpers.CreateOptions(_emailSettings)),
            new FirstFactorEmailProof(
                _bindCodes.Object,
                _userRepository.Object,
                Mock.Of<INotificationService>(MockBehavior.Strict),
                Mock.Of<IOtpGenerator>(MockBehavior.Strict),
                _otpHasher.Object,
                TestHelpers.CreateOptions(_emailSettings),
                TimeProvider.System,
                Mock.Of<ILogger<FirstFactorEmailProof>>()),
            _userRepository.Object,
            _dispatcher.Object,
            Mock.Of<ILogger<EnableTwoFactorCommandHandler>>());
    }

    private static EnableTwoFactorCommand Command(Guid userId, string? emailCode, string totpCode = TotpCode) =>
        new(userId, totpCode, SessionId, "203.0.113.7", emailCode);

    /// <summary>
    /// A recent session, a pending factor with attempts to spare, a correct
    /// authenticator code, recovery codes, and the user.
    /// </summary>
    private void GivenPendingFactor(Guid userId, bool enabled = false)
    {
        _guard
            .Setup(g => g.EnsureRecentSignInAsync(userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, "Firefox on Linux"));
        _stateStore
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                userId, ProtectedSecret, enabled ? "[]" : null, isEnabled: enabled, failedAttempts: 0, lockedUntil: null));
        _stateStore
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _secretProtector
            .Setup(p => p.UnprotectAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlainSecret);
        _totpService.Setup(t => t.ValidateCode(PlainSecret, TotpCode)).Returns(MatchedStep);
        _totpService.Setup(t => t.GenerateRecoveryCodes(10)).Returns(["AAAA-1111"]);
        _totpService.Setup(t => t.HashRecoveryCode(It.IsAny<string>())).Returns<string>(code => $"hash:{code}");
        _userRepository
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId, email: "owner@example.com"));
        _stateStore
            .Setup(s => s.TryEnableAsync(
                userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);
    }

    /// <summary>A live emailed code with attempts to spare, which <paramref name="correct"/> matches.</summary>
    private TwoFactorBindCode GivenLiveCode(Guid userId, string correct = EmailCode)
    {
        var code = new TwoFactorBindCode(
            Guid.NewGuid(), userId, StoredHash, DateTime.UtcNow.AddMinutes(10), null, 0, "203.0.113.7", DateTime.UtcNow);
        _bindCodes
            .Setup(r => r.GetLiveForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);
        _bindCodes
            .Setup(r => r.TryReserveAttemptAsync(code.Id, TwoFactorBindCode.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _otpHasher
            .Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string, string>((scope, submitted, stored) =>
                scope == FirstFactorEmailProof.HashScope(userId) && submitted == correct && stored == StoredHash);
        return code;
    }

    private void VerifyNothingEnabled() =>
        _stateStore.Verify(
            s => s.TryEnableAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // ── Required (email on, switch on) ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Enable_RequiredAndNoEmailCode_ReturnsEmailCodeRequired_ReservingNothing(string? emailCode)
    {
        // Such a request can never succeed, so it costs no attempt — and a client
        // built before the step existed gets a published code rather than a lock.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);

        var result = await _handler.Handle(Command(userId, emailCode), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeRequired.Code);
        _stateStore.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _totpService.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _bindCodes.VerifyNoOtherCalls();
        VerifyNothingEnabled();
    }

    [Fact]
    public async Task Enable_RequiredAndWrongEmailCode_ReturnsEmailCodeInvalid_WithBothReservationsCounted()
    {
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        var code = GivenLiveCode(userId);

        var result = await _handler.Handle(Command(userId, "111111"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        _stateStore.Verify(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()), Times.Once,
            "the pending factor counted the attempt first, and nothing settles it");
        _bindCodes.Verify(r => r.TryReserveAttemptAsync(code.Id, TwoFactorBindCode.MaxAttempts, It.IsAny<CancellationToken>()), Times.Once,
            "the code counted its own attempt before it was checked");
        VerifyNothingEnabled();
        _dispatcher.Verify(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enable_RequiredAndRightEmailCode_Commits_SpendingTheCodeInTheEnableTransaction()
    {
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        var code = GivenLiveCode(userId);

        var result = await _handler.Handle(Command(userId, EmailCode), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().Equal("AAAA-1111");
        // The code is handed to the one transaction that switches the factor on,
        // which consumes it first (TwoFactorStateStoreSqlTests pins the statements).
        _stateStore.Verify(
            s => s.TryEnableAsync(
                userId, ProtectedSecret, It.IsAny<string>(), MatchedStep, true, code.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Enable_TheAuthenticatorCodeIsCheckedFirst_AWrongOneLeavesTheEmailCodeUntouched()
    {
        // Step 5 before step 6: a wrong authenticator code ends the request with
        // its own answer, and the emailed code loses no attempt to it.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        GivenLiveCode(userId);

        var result = await _handler.Handle(Command(userId, EmailCode, totpCode: "000000"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code);
        _bindCodes.Verify(r => r.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingEnabled();
    }

    [Fact]
    public async Task Enable_RequiredAndNoLiveCode_ReturnsEmailCodeInvalid()
    {
        // Never sent, expired, superseded or spent: one answer.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        _bindCodes
            .Setup(r => r.GetLiveForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwoFactorBindCode?)null);

        var result = await _handler.Handle(Command(userId, EmailCode), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        _stateStore.Verify(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNothingEnabled();
    }

    [Fact]
    public async Task Enable_CodeOutOfAttempts_ReturnsEmailCodeInvalid_WithoutCheckingIt()
    {
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        var code = GivenLiveCode(userId);
        _bindCodes
            .Setup(r => r.TryReserveAttemptAsync(code.Id, TwoFactorBindCode.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var result = await _handler.Handle(Command(userId, EmailCode), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        _otpHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingEnabled();
    }

    [Fact]
    public async Task Enable_CodeSpentByAConcurrentEnable_ReturnsEmailCodeInvalid_WithoutCodes()
    {
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        GivenLiveCode(userId);
        _stateStore
            .Setup(s => s.TryEnableAsync(
                userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.ChallengeLost);

        var result = await _handler.Handle(Command(userId, EmailCode), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        _dispatcher.Verify(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enable_WrongEmailCodes_LockThePendingFactorAtTheFifth()
    {
        // The point of checking after the reservation: a stand-in for the A2
        // statement counts each attempt and locks at the fifth, and only a commit
        // settles it. Five wrong emailed codes — with a right authenticator code,
        // as an attacker who ran setup has — lock the factor, so even the right
        // pair is then refused without being checked.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        GivenLiveCode(userId);
        var failures = 0;
        _stateStore
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TwoFactorSnapshot(
                userId, ProtectedSecret, null, isEnabled: false, failures,
                failures >= TwoFactorAuth.MaxFailedAttempts ? DateTime.UtcNow.AddMinutes(15) : null));
        _stateStore
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => failures >= TwoFactorAuth.MaxFailedAttempts ? null : ++failures);

        for (var attempt = 1; attempt <= TwoFactorAuth.MaxFailedAttempts; attempt++)
        {
            var wrong = await _handler.Handle(Command(userId, $"10000{attempt}"), CancellationToken.None);
            wrong.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code, $"attempt {attempt} is a wrong email code");
        }

        var locked = await _handler.Handle(Command(userId, EmailCode), CancellationToken.None);

        locked.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        VerifyNothingEnabled();
    }

    // ── Not required: the PR A behaviour, unchanged ─────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Enable_EmailOffOrSwitchOff_EnablesWithoutACode(bool emailEnabled, bool switchOn)
    {
        // With email off nothing could deliver a code, so the factor binds with the
        // notice alone — no dead end. With the rollout switch off, likewise.
        _emailSettings.Enabled = emailEnabled;
        _twoFactorSettings.RequireEmailCodeForFirstFactor = switchOn;
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);

        var result = await _handler.Handle(Command(userId, emailCode: null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _stateStore.Verify(
            s => s.TryEnableAsync(
                userId, ProtectedSecret, It.IsAny<string>(), MatchedStep, true, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _bindCodes.VerifyNoOtherCalls();
    }

    // ── An enabled factor: no email step (the reachable half of FA2) ────────

    [Theory]
    [InlineData(null)]
    [InlineData(EmailCode)]
    public async Task Enable_FactorAlreadyEnabled_ReturnsAlreadyEnabled_WithoutTouchingTheCodeTable(string? emailCode)
    {
        // Before S20 the only qualifying factor is an enabled TOTP row, and enable
        // refuses it: nothing is reserved on, or consumed from, the code table.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId, enabled: true);

        var result = await _handler.Handle(Command(userId, emailCode), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorAlreadyEnabled.Code);
        _stateStore.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _bindCodes.VerifyNoOtherCalls();
        VerifyNothingEnabled();
    }

    [Fact]
    public void TheEmailProof_IsNeverAFactor_EnableReachesNoTokenOrSession()
    {
        // The code proves the mailbox for the bind and nothing else (D2): neither
        // the handler nor the proof can mint a token, open or end a session, or
        // touch a refresh credential. Asserted on what they are given to call.
        Type[] forbidden =
        [
            typeof(IJwtTokenService),
            typeof(ILoginResponseBuilder),
            typeof(IUserSessionRepository),
            typeof(IIdpSessionRepository),
            typeof(IRefreshTokenRepository),
            typeof(ICredentialRevocationService),
        ];

        foreach (var type in new[] { typeof(EnableTwoFactorCommandHandler), typeof(FirstFactorEmailProof) })
        {
            var dependencies = type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
            dependencies.Should().NotBeEmpty();
            dependencies.Should().NotContain(forbidden, $"{type.Name} must not reach a token or a session");
        }
    }

    [Fact]
    public void TheCommand_KeepsBothCodesOutOfItsText()
    {
        var text = Command(Guid.NewGuid(), EmailCode).ToString();

        text.Should().NotContain(EmailCode).And.NotContain(TotpCode);
    }
}
