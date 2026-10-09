using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.StepUpTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// S08 T7: a second factor proved inside the current session. The session is
/// checked before anything is counted; a session that already proved two factors
/// answers success without a code; otherwise one attempt is reserved, the code
/// checked, and the factor settled and the session upgraded in ONE store call.
/// The real verifier runs under the handler.
/// </summary>
public class StepUpTwoFactorCommandHandlerTests
{
    private const string ProtectedSecret = "v2:protected-secret";
    private const string PlainSecret = "TESTSECRET";
    private const long MatchedStep = 59_313_872;
    private const string StoredCodes = "[ \"hash-1\", \"hash-2\" ]";
    private const string IdpCookie = "idp-cookie";

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly Mock<MediatR.IPublisher> _publisher = new();
    private readonly Mock<ILogger<StepUpTwoFactorCommandHandler>> _logger = new();
    private readonly TwoFactorSettings _settings = new();
    private readonly StepUpTwoFactorCommandHandler _handler;

    public StepUpTwoFactorCommandHandlerTests()
    {
        var verifier = new SecondFactorVerifier(
            _store.Object,
            [
                new TotpProofStrategy(_totp.Object, _protector.Object),
                new RecoveryCodeProofStrategy(_totp.Object)
            ]);
        _keys.Setup(k => k.ComputeTokenHash(IdpCookie)).Returns("idp-hash");

        _handler = new StepUpTwoFactorCommandHandler(
            _sessions.Object,
            verifier,
            _store.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_settings)),
            _keys.Object,
            _publisher.Object,
            _logger.Object);
    }

    private static StepUpTwoFactorCommand Command(string code = "123456", bool recovery = false, Guid? sessionId = null) =>
        new(UserId, code, recovery, sessionId ?? SessionId, IdpCookie, "203.0.113.7");

    private void GivenSession(AuthenticationMethods methods, bool isActive = true, Guid? userId = null) =>
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: SessionId, userId: userId ?? UserId, isActive: isActive,
                terminatedAt: isActive ? null : DateTime.UtcNow, methods: methods));

    private void GivenFactor(DateTime? lockedUntil = null)
    {
        _store.Setup(s => s.GetSnapshotAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(UserId, ProtectedSecret, StoredCodes, isEnabled: true, failedAttempts: 0, lockedUntil));
        _store.Setup(s => s.TryReserveAttemptAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _protector.Setup(p => p.UnprotectAsync(UserId, ProtectedSecret, It.IsAny<CancellationToken>())).ReturnsAsync(PlainSecret);
        _totp.Setup(t => t.ValidateCode(PlainSecret, "123456")).Returns(MatchedStep);
        _totp.Setup(t => t.VerifyRecoveryCode("RECOVERY-1", "hash-1")).Returns(true);
    }

    private void GivenCommit(LoginCommitOutcome outcome) =>
        _store.Setup(s => s.TryCommitStepUpAsync(
                It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    private void VerifyNothingReserved()
    {
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.GetSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryCommitStepUpAsync(
            It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The pre-check: no reservation ───────────────────────────────────────

    [Fact]
    public async Task Handle_NoSessionInTheToken_AsksToSignInAgain_WithoutAReservation()
    {
        var result = await _handler.Handle(Command() with { CurrentSessionId = null }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingReserved();
    }

    [Fact]
    public async Task Handle_MissingSessionRow_AsksToSignInAgain_WithoutAReservation()
    {
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>())).ReturnsAsync((UserSession?)null);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        VerifyNothingReserved();
    }

    [Fact]
    public async Task Handle_SomeoneElsesSession_AsksToSignInAgain_WithoutAReservation()
    {
        GivenSession(AuthenticationMethods.Password, userId: Guid.NewGuid());

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingReserved();
    }

    [Fact]
    public async Task Handle_EndedSession_AsksToSignInAgain_WithoutAReservation()
    {
        GivenSession(AuthenticationMethods.Password, isActive: false);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingReserved();
    }

    [Theory]
    [InlineData(0)]   // a session recorded before methods were
    [InlineData(4)]   // an emailed code first
    public async Task Handle_SessionWithoutAFirstFactor_AsksToSignInAgain_WithoutAReservation(int stored)
    {
        GivenSession(AuthenticationMethods.FromStored(stored));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingReserved();
    }

    [Fact]
    public async Task Handle_SessionAlreadyMfa_SucceedsWithoutCheckingTheCode()
    {
        // Another tab stepped up a moment ago: no code is checked, nothing counted.
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        var result = await _handler.Handle(Command(code: "000000"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyNothingReserved();
    }

    // ── The commit ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CorrectTotp_CommitsTheFactorAndTheSessionInOneStoreCall()
    {
        GivenSession(AuthenticationMethods.Password);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.Committed);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryCommitStepUpAsync(
                UserId,
                It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.Totp && p.Step == MatchedStep),
                true,
                It.Is<SessionUpgrade>(u => u.SessionId == SessionId && u.IdpTokenHash == "idp-hash"
                    && u.Method == AuthenticationMethods.Totp),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the session upgrade is inside the store's transaction, never a separate write");
        _sessions.Verify(r => r.UpdateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()), Times.Never,
            "the session row is never written outside that transaction");
    }

    [Fact]
    public async Task Handle_CorrectRecoveryCode_UpgradesWithRecoveryCode()
    {
        GivenSession(AuthenticationMethods.ExternalIdentity);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.Committed);

        var result = await _handler.Handle(Command(code: "RECOVERY-1", recovery: true), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryCommitStepUpAsync(
                UserId,
                It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.RecoveryCode && p.OldCodesJson == StoredCodes),
                It.IsAny<bool>(),
                It.Is<SessionUpgrade>(u => u.Method == AuthenticationMethods.RecoveryCode),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WrongCode_LeavesTheReservation_AndCommitsNothing()
    {
        GivenSession(AuthenticationMethods.Password);
        GivenFactor();

        var result = await _handler.Handle(Command(code: "999999"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _store.Verify(s => s.TryReserveAttemptAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.TryCommitStepUpAsync(
            It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoEnabledFactor_AnswersTwoFactorNotEnabled()
    {
        GivenSession(AuthenticationMethods.Password);
        _store.Setup(s => s.GetSnapshotAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((TwoFactorSnapshot?)null);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorNotEnabled.Code);
    }

    [Fact]
    public async Task Handle_LockedFactor_AnswersLockedOut()
    {
        GivenSession(AuthenticationMethods.Password);
        GivenFactor(lockedUntil: DateTime.UtcNow.AddMinutes(10));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
    }

    public static TheoryData<LoginCommitOutcome, string> Refusals => new()
    {
        { LoginCommitOutcome.StepReused, TwoFactorErrors.CodeAlreadyUsed.Code },
        { LoginCommitOutcome.RecoveryCodesChanged, TwoFactorErrors.InvalidRecoveryCode.Code },
        { LoginCommitOutcome.SessionLost, AuthErrors.ReauthenticationRequired.Code },
        { LoginCommitOutcome.FactorLost, UserErrors.TwoFactorNotEnabled.Code },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Handle_RefusedCommit_NamesWhy(LoginCommitOutcome outcome, string code)
    {
        GivenSession(AuthenticationMethods.Password);
        GivenFactor();
        GivenCommit(outcome);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
    }

    [Fact]
    public async Task Handle_ReusedCodeWithTheSwitchOff_SucceedsAndLogsTheReuse()
    {
        // Window W of X01: with RejectReusedCodes off a reused code settles, and the
        // line an operator reviews is written on step-up too.
        _settings.RejectReusedCodes = false;
        GivenSession(AuthenticationMethods.Password);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.ReuseAccepted);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryCommitStepUpAsync(
            UserId, It.IsAny<SecondFactorProof>(), false, It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Reused two-factor code accepted")
                    && v.ToString()!.Contains("step-up") && !v.ToString()!.Contains("123456")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoSsoCookie_UpgradesTheSessionRowOnly()
    {
        GivenSession(AuthenticationMethods.Password);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.Committed);

        var result = await _handler.Handle(Command() with { IdpSessionToken = null }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryCommitStepUpAsync(
            UserId, It.IsAny<SecondFactorProof>(), It.IsAny<bool>(),
            It.Is<SessionUpgrade>(u => u.IdpTokenHash == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── D-B8: the audit row, after the commit; never for what was not proved ──

    [Fact]
    public async Task Handle_Committed_PublishesTheStepUp_ForTheAuditRow_AfterTheCommit()
    {
        var calls = new List<string>();
        GivenSession(AuthenticationMethods.Password);
        GivenFactor();
        _store.Setup(s => s.TryCommitStepUpAsync(
                It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("commit"))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        _publisher.Setup(p => p.Publish(It.IsAny<Auth.Domain.Events.TwoFactorSteppedUpEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("publish"))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        calls.Should().Equal("commit", "publish");
        _publisher.Verify(p => p.Publish(
            It.Is<Auth.Domain.Events.TwoFactorSteppedUpEvent>(e => e.UserId == UserId && e.SessionId == SessionId && e.Method == SecondFactorMethod.Totp),
            CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_NothingProved_PublishesNothing(bool alreadyTwoFactor)
    {
        GivenSession(alreadyTwoFactor ? AuthenticationMethods.Password.With(AuthenticationMethods.Totp) : AuthenticationMethods.Password);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.StepReused);

        await _handler.Handle(Command(), CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<Auth.Domain.Events.TwoFactorSteppedUpEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
