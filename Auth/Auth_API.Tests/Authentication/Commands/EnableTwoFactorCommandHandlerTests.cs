using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EnableTwoFactor;
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
/// Unit tests for EnableTwoFactorCommandHandler. The real two-phase verifier and
/// its TOTP strategy run under the handler; only storage, TOTP arithmetic, hashing
/// and decryption are stubbed.
/// </summary>
public class EnableTwoFactorCommandHandlerTests
{
    private const string ProtectedSecret = "v2:pending-secret";
    private const string PlainSecret = "TESTSECRET";
    private const long MatchedStep = 59_313_872;
    private const string ClientIp = "203.0.113.7";
    private const string DeviceName = "Firefox on Linux";

    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly string[] GeneratedCodes = ["AAAA-1111", "BBBB-2222"];

    private readonly Mock<IReauthenticationGuard> _guardMock = new();
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock = new();
    private readonly Mock<ITotpService> _totpServiceMock = new();
    private readonly Mock<ITwoFactorSecretProtector> _secretProtectorMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly Mock<ILogger<EnableTwoFactorCommandHandler>> _loggerMock = new();
    private readonly TwoFactorSettings _twoFactorSettings = new();
    private readonly EnableTwoFactorCommandHandler _handler;

    public EnableTwoFactorCommandHandlerTests()
    {
        var verifier = new SecondFactorVerifier(
            _stateStoreMock.Object,
            [
                new TotpProofStrategy(_totpServiceMock.Object, _secretProtectorMock.Object),
                new RecoveryCodeProofStrategy(_totpServiceMock.Object)
            ]);

        _handler = new EnableTwoFactorCommandHandler(
            _guardMock.Object,
            verifier,
            _stateStoreMock.Object,
            _totpServiceMock.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_twoFactorSettings)),
            _userRepositoryMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    private static EnableTwoFactorCommand CreateCommand(Guid userId, string code = "123456") =>
        new(userId, code, SessionId, ClientIp);

    /// <summary>
    /// A recent session, a pending (not enabled) factor with an attempt to spare,
    /// a correct code, recovery codes to hand out, and the user.
    /// </summary>
    private User GivenPendingFactor(Guid userId)
    {
        _guardMock
            .Setup(g => g.EnsureRecentSignInAsync(userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, DeviceName));
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                userId, ProtectedSecret, recoveryCodes: null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _secretProtectorMock
            .Setup(p => p.UnprotectAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlainSecret);
        _totpServiceMock.Setup(t => t.ValidateCode(PlainSecret, "123456")).Returns(MatchedStep);
        _totpServiceMock.Setup(t => t.GenerateRecoveryCodes(10)).Returns(GeneratedCodes);
        _totpServiceMock.Setup(t => t.HashRecoveryCode(It.IsAny<string>())).Returns<string>(code => $"hash:{code}");

        var user = TestHelpers.CreateUser(id: userId, email: "owner@example.com");
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return user;
    }

    private void GivenCommit(Guid userId, LoginCommitOutcome outcome) =>
        _stateStoreMock
            .Setup(s => s.TryEnableAsync(
                userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    // ── T4: enable checks the code like any second factor ───────────────────

    [Fact]
    public async Task Enable_OnPendingRow_ReservesAndVerifies()
    {
        // The pending row is reserved — not an enabled one — and the code is
        // checked against its secret; the commit compares the secret it saw.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.Committed);
        var order = new List<string>();
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("reserve"))
            .ReturnsAsync(1);
        _totpServiceMock
            .Setup(t => t.ValidateCode(PlainSecret, "123456"))
            .Callback(() => order.Add("verify"))
            .Returns(MatchedStep);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().Equal(GeneratedCodes);
        order.Should().Equal("reserve", "verify");
        _stateStoreMock.Verify(
            s => s.TryEnableAsync(
                userId,
                ProtectedSecret,
                "[\"hash:AAAA-1111\",\"hash:BBBB-2222\"]",
                MatchedStep,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Enable_WrongCode_LocksAtFifth()
    {
        // A stand-in for the A2 statement: each reservation counts, the fifth sets
        // the lock, a locked factor reserves nothing; a commit clears the count, as
        // the real statements do. Five wrong codes in a row lock the pending factor,
        // so even the right code is then refused without being checked.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        var failures = 0;
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TwoFactorSnapshot(
                userId, ProtectedSecret, null, isEnabled: false, failures,
                failures >= TwoFactorAuth.MaxFailedAttempts ? DateTime.UtcNow.AddMinutes(15) : null));
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => failures >= TwoFactorAuth.MaxFailedAttempts ? null : ++failures);
        _stateStoreMock
            .Setup(s => s.TryEnableAsync(
                userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => failures = 0)
            .ReturnsAsync(LoginCommitOutcome.Committed);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => failures = 0)
            .ReturnsAsync(LoginCommitOutcome.Committed);

        for (var attempt = 1; attempt <= TwoFactorAuth.MaxFailedAttempts; attempt++)
        {
            var wrong = await _handler.Handle(CreateCommand(userId, "000000"), CancellationToken.None);
            wrong.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code, $"attempt {attempt} is a wrong code");
        }

        failures.Should().Be(TwoFactorAuth.MaxFailedAttempts, "no wrong code may settle the count");

        var locked = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        locked.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        _totpServiceMock.Verify(t => t.ValidateCode(PlainSecret, "123456"), Times.Never);
        _stateStoreMock.Verify(
            s => s.TryEnableAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(LoginCommitOutcome.AlreadyEnabled, "User.TwoFactorAlreadyEnabled")]
    [InlineData(LoginCommitOutcome.FactorLost, "TwoFactor.SetupRequired")]
    public async Task Enable_LostRace_Returns409_WithoutCodes(LoginCommitOutcome outcome, string expectedCode)
    {
        // Another tab enabled the factor first (409), or replaced the pending secret
        // after this code was checked (SetupRequired). Either way the codes this
        // request generated are not the stored ones, and are never shown.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        GivenCommit(userId, outcome);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(expectedCode);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyEnabledBeforeTheCheck_Returns409_WithoutVerifying()
    {
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(userId, ProtectedSecret, "[]", isEnabled: true, failedAttempts: 0, lockedUntil: null));

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorAlreadyEnabled.Code);
        _totpServiceMock.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPendingFactor_ReturnsSetupRequired()
    {
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwoFactorSnapshot?)null);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.SetupRequired.Code);
    }

    // ── OI-40 (1): the step is claimed in the commit, not after it ──────────

    [Fact]
    public async Task Handle_DispatchThrowsAfterCommit_TheStepWasWrittenInTheCommit()
    {
        // The code that switched the factor on must not sign in afterwards. Its step
        // is written by the same transaction that enables the factor — before the
        // events run — so a failure in them (or a client that leaves) can no longer
        // leave the code usable for up to 90 seconds.
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        var order = new List<string>();
        _stateStoreMock
            .Setup(s => s.TryEnableAsync(
                userId, ProtectedSecret, It.IsAny<string>(), MatchedStep, true, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("commit with the step"))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        _eventDispatcherMock
            .Setup(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("dispatch"))
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));

        var act = () => _handler.Handle(CreateCommand(userId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        order.Should().Equal("commit with the step", "dispatch");
        _stateStoreMock.Verify(
            s => s.TryClaimTotpStepAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "no separate claim runs after the events any more");
    }

    // ── With the rollout switch off ─────────────────────────────────────────

    [Fact]
    public async Task Handle_ReuseAcceptedWithTheSwitchOff_EnablesAndLogsTheAcceptedLine()
    {
        _twoFactorSettings.RejectReusedCodes = false;
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        _stateStoreMock
            .Setup(s => s.TryEnableAsync(
                userId, It.IsAny<string>(), It.IsAny<string>(), MatchedStep, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.ReuseAccepted);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Reused two-factor code accepted (RejectReusedCodes=false)")
                    && v.ToString()!.Contains("enable")
                    && v.ToString()!.Contains(ClientIp)
                    && !v.ToString()!.Contains("123456")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── What follows a commit ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_CodesHashedBeforeTheCommit_EventRaisedAfterIt()
    {
        var userId = Guid.NewGuid();
        var user = GivenPendingFactor(userId);
        var order = new List<string>();
        _totpServiceMock
            .Setup(t => t.HashRecoveryCode(It.IsAny<string>()))
            .Callback(() => order.Add("hash"))
            .Returns<string>(code => $"hash:{code}");
        _stateStoreMock
            .Setup(s => s.TryEnableAsync(
                userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add($"commit:{user.DomainEvents.Count}"))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        TwoFactorEnabledEvent? raised = null;
        _eventDispatcherMock
            .Setup(d => d.DispatchEventsAsync(user, CancellationToken.None))
            .Callback(() => raised = user.DomainEvents.OfType<TwoFactorEnabledEvent>().SingleOrDefault())
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(CreateCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        order.Should().Equal("hash", "hash", "commit:0");
        raised.Should().NotBeNull("the event is raised once the commit has written");
        raised!.EnabledBy.Should().Be(userId);
        raised.Email.Should().Be("owner@example.com");
        raised.DeviceName.Should().Be(DeviceName);
        user.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CommitUsesTheRequestToken_TheEventsRunWithoutIt()
    {
        var userId = Guid.NewGuid();
        GivenPendingFactor(userId);
        GivenCommit(userId, LoginCommitOutcome.Committed);
        using var cts = new CancellationTokenSource();

        await _handler.Handle(CreateCommand(userId), cts.Token);

        _guardMock.Verify(g => g.EnsureRecentSignInAsync(userId, SessionId, cts.Token), Times.Once);
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(userId, cts.Token), Times.Once);
        _stateStoreMock.Verify(
            s => s.TryEnableAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), cts.Token),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), CancellationToken.None), Times.Once);
    }
}
