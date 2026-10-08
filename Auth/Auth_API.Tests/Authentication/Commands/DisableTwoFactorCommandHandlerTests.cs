using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for DisableTwoFactorCommandHandler. The real two-phase verifier and
/// its strategies run under the handler; only storage, TOTP arithmetic, hashing
/// and decryption are stubbed, so these tests follow a code from the reservation
/// through the check to the commit — and what must follow a commit.
/// </summary>
public class DisableTwoFactorCommandHandlerTests
{
    private const string ProtectedSecret = "v2:protected-secret";
    private const string PlainSecret = "TESTSECRET";
    private const long MatchedStep = 59_313_872;
    private const string IdpSessionToken = "idp-session-cookie";
    private const string ClientIp = "203.0.113.7";
    private const string DeviceName = "Chrome on Windows";

    // The stored recovery-code set, byte for byte — a recovery proof must carry
    // exactly this text, never a re-serialization of it.
    private const string StoredCodes = "[ \"hash-1\", \"hash-2\" ]";

    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IReauthenticationGuard> _guardMock = new();
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock = new();
    private readonly Mock<ITotpService> _totpServiceMock = new();
    private readonly Mock<ITwoFactorSecretProtector> _secretProtectorMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _revocationMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly Mock<ILogger<DisableTwoFactorCommandHandler>> _loggerMock = new();
    private readonly Mock<IPlatformMfaPolicy> _platformMfaPolicyMock = new();
    private readonly TwoFactorSettings _twoFactorSettings = new();
    private readonly DisableTwoFactorCommandHandler _handler;

    public DisableTwoFactorCommandHandlerTests()
    {
        var verifier = new SecondFactorVerifier(
            _stateStoreMock.Object,
            [
                new TotpProofStrategy(_totpServiceMock.Object, _secretProtectorMock.Object),
                new RecoveryCodeProofStrategy(_totpServiceMock.Object)
            ]);

        _handler = new DisableTwoFactorCommandHandler(
            _guardMock.Object,
            _platformMfaPolicyMock.Object,
            verifier,
            _stateStoreMock.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_twoFactorSettings)),
            _userRepositoryMock.Object,
            _revocationMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    private static DisableTwoFactorCommand CreateCommand(Guid userId, string code = "123456", bool useRecoveryCode = false) =>
        new(userId, code, useRecoveryCode, SessionId, IdpSessionToken, ClientIp);

    /// <summary>
    /// A recent session, an enabled factor that is not locked and has one attempt
    /// to spare, a correct TOTP code and a recovery code that matches, and the
    /// user — everything a disable needs up to the commit, whose answer each test
    /// states.
    /// </summary>
    private User GivenEnabledFactor(Guid userId, DateTime? lockedUntil = null)
    {
        _guardMock
            .Setup(g => g.EnsureRecentSignInAsync(userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, DeviceName));
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                userId, ProtectedSecret, StoredCodes, isEnabled: true, failedAttempts: 0, lockedUntil));
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _secretProtectorMock
            .Setup(p => p.UnprotectAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlainSecret);
        _totpServiceMock.Setup(t => t.ValidateCode(PlainSecret, "123456")).Returns(MatchedStep);
        _totpServiceMock.Setup(t => t.VerifyRecoveryCode("RECOVERY-1", "hash-1")).Returns(true);

        var user = TestHelpers.CreateUser(id: userId, email: "owner@example.com", twoFactorEnabled: true);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return user;
    }

    private void GivenCommit(Guid userId, LoginCommitOutcome outcome) =>
        _stateStoreMock
            .Setup(s => s.TryDisableAsync(userId, It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    private void VerifyNothingAfterTheCommit()
    {
        _revocationMock.Verify(
            r => r.RevokeCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void VerifyWarning(string text, Times times) =>
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(text) && !v.ToString()!.Contains("123456")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    // ── T1: a recovery code switches the factor off ─────────────────────────

    [Fact]
    public async Task Disable_RecoveryCode_CommitsWithOldCodes()
    {
        // A user whose phone is gone still holds recovery codes. The proof carries
        // the stored set exactly as read, so the commit removes the factor only
        // while that set is still the one the code was checked against.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        SecondFactorProof? committed = null;
        _stateStoreMock
            .Setup(s => s.TryDisableAsync(userId, It.IsAny<SecondFactorProof>(), true, It.IsAny<CancellationToken>()))
            .Callback<Guid, SecondFactorProof, bool, CancellationToken>((_, proof, _, _) => committed = proof)
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await _handler.Handle(CreateCommand(userId, "RECOVERY-1", useRecoveryCode: true), CancellationToken.None);

        result.IsError.Should().BeFalse();
        committed.Should().NotBeNull();
        committed!.Method.Should().Be(SecondFactorMethod.RecoveryCode);
        committed.OldCodesJson.Should().Be(StoredCodes, "the commit compares the row against the text exactly as read");
        _totpServiceMock.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TotpCode_CommitsTheMatchedStep()
    {
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.Committed);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _stateStoreMock.Verify(
            s => s.TryDisableAsync(
                userId,
                It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.Totp && p.Step == MatchedStep),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Refusals before the commit ─────────────────────────────────────────

    [Fact]
    public async Task Handle_LockedFactor_ReturnsLockedOut_WithoutVerifying()
    {
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId, lockedUntil: DateTime.UtcNow.AddMinutes(10));

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _totpServiceMock.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _stateStoreMock.Verify(
            s => s.TryDisableAsync(It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReservationRefused_ReturnsLockedOut_WithoutVerifying()
    {
        // The read saw an unlocked factor, but a burst of requests locked it first:
        // the reservation decides, and nothing is checked.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        _totpServiceMock.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoEnabledFactor_ReturnsTwoFactorNotEnabled()
    {
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwoFactorSnapshot?)null);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorNotEnabled.Code);
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WrongCode_StaysCounted_AndCommitsNothing()
    {
        // The attempt was reserved before the code was checked, and a wrong code
        // settles nothing: the failure stays counted towards the lock.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);

        var result = await _handler.Handle(CreateCommand(userId, "000000"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code);
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _stateStoreMock.Verify(
            s => s.TryDisableAsync(It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNothingAfterTheCommit();
    }

    [Fact]
    public async Task Handle_WrongRecoveryCode_ReturnsInvalidRecoveryCode()
    {
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);

        var result = await _handler.Handle(CreateCommand(userId, "NOT-A-CODE", useRecoveryCode: true), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.InvalidRecoveryCode.Code);
        VerifyNothingAfterTheCommit();
    }

    // ── Refusals at the commit ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_StepReused_ReturnsCodeAlreadyUsed_AndLogsTheRejection()
    {
        // The code the owner just signed in with, presented again by someone who
        // holds the password and a session: refused like a wrong code.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.StepReused);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.CodeAlreadyUsed.Code);
        VerifyNothingAfterTheCommit();
        VerifyWarning("Reused two-factor code rejected", Times.Once());
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("disable") && v.ToString()!.Contains(ClientIp)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RecoveryCodesChangedUnderTheCommit_ReturnsInvalidRecoveryCode()
    {
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.RecoveryCodesChanged);

        var result = await _handler.Handle(CreateCommand(userId, "RECOVERY-1", useRecoveryCode: true), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.InvalidRecoveryCode.Code);
        VerifyNothingAfterTheCommit();
    }

    [Theory]
    [InlineData(LoginCommitOutcome.FactorLost)]
    [InlineData(LoginCommitOutcome.ChallengeLost)]
    [InlineData(LoginCommitOutcome.AlreadyEnabled)]
    public async Task Handle_AnyOtherCommitOutcome_SwitchesNothingOff(LoginCommitOutcome outcome)
    {
        // Fail-closed: only a commit that wrote is followed by the event, the
        // revocation and the notice.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        GivenCommit(userId, outcome);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorNotEnabled.Code);
        VerifyNothingAfterTheCommit();
    }

    // ── With the rollout switch off ─────────────────────────────────────────

    [Fact]
    public async Task Handle_ReuseAcceptedWithTheSwitchOff_DisablesAndLogsTheAcceptedLine()
    {
        // TwoFactor:RejectReusedCodes off: the store let the reused step through.
        // The line the owner reviews weekly must still be written on this surface.
        _twoFactorSettings.RejectReusedCodes = false;
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        _stateStoreMock
            .Setup(s => s.TryDisableAsync(userId, It.IsAny<SecondFactorProof>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.ReuseAccepted);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyWarning("Reused two-factor code accepted (RejectReusedCodes=false)", Times.Once());
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), CancellationToken.None), Times.Once);
    }

    // ── T3: after the commit ───────────────────────────────────────────────

    [Fact]
    public async Task Disable_RevokesOthers_SparingCurrent()
    {
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.Committed);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _revocationMock.Verify(
            r => r.RevokeCredentialsAsync(
                userId,
                SessionId,
                IdpSessionToken,
                userId,
                "Two-factor disabled",
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task Disable_RevokesBeforeDispatch()
    {
        // The commit, then the revocation, then the events: the sessions an
        // attacker holds are gone before anything else runs, and the notice says so.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        var order = new List<string>();
        _stateStoreMock
            .Setup(s => s.TryDisableAsync(userId, It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("commit"))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        _revocationMock
            .Setup(r => r.RevokeCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("revoke"))
            .ReturnsAsync(2);
        _eventDispatcherMock
            .Setup(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("dispatch"))
            .Returns(Task.CompletedTask);

        await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        order.Should().Equal("commit", "revoke", "dispatch");
    }

    [Fact]
    public async Task Disable_RevocationThrows_StillSucceeds()
    {
        // The factor is already off. A revocation that fails must not cost the
        // owner the email, nor turn a completed change into an error.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.Committed);
        _revocationMock
            .Setup(r => r.RevokeCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), CancellationToken.None), Times.Once);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(userId.ToString())),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_CommitUsesTheRequestToken_TheRestRunsWithoutIt()
    {
        // Everything up to the commit stops when the caller goes away; nothing after
        // it may, because the factor is already off.
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.Committed);
        using var cts = new CancellationTokenSource();

        var result = await _handler.Handle(CreateCommand(userId), cts.Token);

        result.IsError.Should().BeFalse();
        _guardMock.Verify(g => g.EnsureRecentSignInAsync(userId, SessionId, cts.Token), Times.Once);
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(userId, cts.Token), Times.Once);
        _userRepositoryMock.Verify(r => r.GetByIdAsync(userId, cts.Token), Times.Once);
        _stateStoreMock.Verify(
            s => s.TryDisableAsync(userId, It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), cts.Token), Times.Once);
        _revocationMock.Verify(
            r => r.RevokeCredentialsAsync(userId, SessionId, IdpSessionToken, userId, It.IsAny<string>(), CancellationToken.None),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_RaisesTheDisabledEvent_WithTheSessionsDevice()
    {
        var userId = Guid.NewGuid();
        var user = GivenEnabledFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.Committed);
        TwoFactorDisabledEvent? raised = null;
        _eventDispatcherMock
            .Setup(d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()))
            .Callback(() => raised = user.DomainEvents.OfType<TwoFactorDisabledEvent>().SingleOrDefault())
            .Returns(Task.CompletedTask);

        await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        raised.Should().NotBeNull();
        raised!.UserId.Should().Be(userId);
        raised.DisabledBy.Should().Be(userId);
        raised.Email.Should().Be("owner@example.com");
        raised.DeviceName.Should().Be(DeviceName);
        user.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_StaleSession_ReturnsReauthenticationRequired()
    {
        var userId = Guid.NewGuid();
        GivenEnabledFactor(userId);
        _guardMock
            .Setup(g => g.EnsureRecentSignInAsync(userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        _stateStoreMock.Verify(s => s.GetSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
