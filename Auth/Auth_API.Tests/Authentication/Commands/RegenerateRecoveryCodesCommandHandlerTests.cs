using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.RegenerateRecoveryCodes;
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
/// S08 T8: new recovery codes need a recent session that proved two factors, and
/// one more proof of the factor. The real reauthentication guard and verifier run
/// under the handler, so the session rules are the guard's own.
/// </summary>
public class RegenerateRecoveryCodesCommandHandlerTests
{
    private const string ProtectedSecret = "v2:protected-secret";
    private const string PlainSecret = "TESTSECRET";
    private const long MatchedStep = 59_313_872;
    private const string StoredCodes = "[\"hash-1\",\"hash-2\"]";

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();
    private readonly Mock<ILogger<RegenerateRecoveryCodesCommandHandler>> _logger = new();
    private readonly TwoFactorSettings _settings = new();
    private readonly User _user = TestHelpers.CreateUser(id: UserId, email: "owner@example.org", twoFactorEnabled: true);
    private readonly List<IDomainEvent> _dispatched = [];
    private readonly RegenerateRecoveryCodesCommandHandler _handler;

    public RegenerateRecoveryCodesCommandHandlerTests()
    {
        var verifier = new SecondFactorVerifier(
            _store.Object,
            [
                new TotpProofStrategy(_totp.Object, _protector.Object),
                new RecoveryCodeProofStrategy(_totp.Object)
            ]);
        var guard = new ReauthenticationGuard(
            _sessions.Object,
            TestHelpers.CreateOptions(_settings),
            new FixedTimeProvider(new DateTimeOffset(Now)),
            Mock.Of<ILogger<ReauthenticationGuard>>());

        _users.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _dispatcher
            .Setup(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Callback<AggregateRoot, CancellationToken>((root, _) => _dispatched.AddRange(root.DomainEvents))
            .Returns(Task.CompletedTask);
        _totp.Setup(t => t.GenerateRecoveryCodes(It.IsAny<int>())).Returns(["NEW-1", "NEW-2"]);
        _totp.Setup(t => t.HashRecoveryCode(It.IsAny<string>())).Returns<string>(code => $"h({code})");

        _handler = new RegenerateRecoveryCodesCommandHandler(
            guard,
            verifier,
            _store.Object,
            _totp.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_settings)),
            _users.Object,
            _dispatcher.Object,
            _logger.Object);
    }

    private static RegenerateRecoveryCodesCommand Command(string code = "123456", bool recovery = false) =>
        new(UserId, code, recovery, SessionId, "203.0.113.7");

    private void GivenSession(AuthenticationMethods methods, int signedInMinutesAgo = 2) =>
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: SessionId, userId: UserId, createdAt: Now.AddMinutes(-signedInMinutesAgo), methods: methods));

    private static AuthenticationMethods TwoFactors => AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

    private void GivenFactor()
    {
        _store.Setup(s => s.GetSnapshotAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(UserId, ProtectedSecret, StoredCodes, isEnabled: true, failedAttempts: 0, lockedUntil: null));
        _store.Setup(s => s.TryReserveAttemptAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _protector.Setup(p => p.UnprotectAsync(UserId, ProtectedSecret, It.IsAny<CancellationToken>())).ReturnsAsync(PlainSecret);
        _totp.Setup(t => t.ValidateCode(PlainSecret, "123456")).Returns(MatchedStep);
        _totp.Setup(t => t.VerifyRecoveryCode("RECOVERY-1", "hash-1")).Returns(true);
    }

    private void GivenCommit(LoginCommitOutcome outcome) =>
        _store.Setup(s => s.TryRegenerateCodesAsync(
                It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    private void VerifyNothingCounted()
    {
        _store.Verify(s => s.GetSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryRegenerateCodesAsync(
            It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The session: recent, and two factors ────────────────────────────────

    [Fact]
    public async Task Handle_SessionThatProvedOnlyThePassword_AsksToSignInAgain_BeforeAnythingIsCounted()
    {
        GivenSession(AuthenticationMethods.Password);
        GivenFactor();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_TwoFactorSessionTooOld_AsksToSignInAgain_BeforeAnythingIsCounted()
    {
        GivenSession(TwoFactors, signedInMinutesAgo: 16);
        GivenFactor();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_SessionFromBeforeMethodsWereRecorded_AsksToSignInAgain()
    {
        GivenSession(AuthenticationMethods.Unknown);
        GivenFactor();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingCounted();
    }

    // ── The proof and the commit ────────────────────────────────────────────

    [Fact]
    public async Task Handle_WrongCode_LeavesTheAttemptCounted_AndWritesNothing()
    {
        GivenSession(TwoFactors);
        GivenFactor();

        var result = await _handler.Handle(Command("999999"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code);
        _store.Verify(s => s.TryReserveAttemptAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.TryRegenerateCodesAsync(
            It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TotpCode_ReplacesTheSetSeen_AndShowsTheNewCodes_AndTellsTheOwner()
    {
        GivenSession(TwoFactors);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.Committed);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().Equal("NEW-1", "NEW-2");
        _store.Verify(s => s.TryRegenerateCodesAsync(
            UserId,
            It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.Totp && p.Step == MatchedStep),
            true,
            StoredCodes,
            JsonSerializer.Serialize(new[] { "h(NEW-1)", "h(NEW-2)" }),
            It.IsAny<CancellationToken>()), Times.Once);
        _dispatched.Should().ContainSingle().Which.Should().BeOfType<TwoFactorRecoveryCodesRegeneratedEvent>()
            .Which.RegeneratedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task Handle_RecoveryCode_SpendsIt_AndTheSpentCodeIsNotInTheProofsSet()
    {
        GivenSession(TwoFactors);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.Committed);

        var result = await _handler.Handle(Command("RECOVERY-1", recovery: true), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryRegenerateCodesAsync(
            UserId,
            It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.RecoveryCode
                                          && p.OldCodesJson == StoredCodes
                                          && !p.NewCodesJson!.Contains("hash-1")),
            It.IsAny<bool>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReusedCodeWithTheSwitchOff_SucceedsAndLogsTheReuse()
    {
        // Window W of X01: with RejectReusedCodes off a reused code settles, and the
        // line an operator reviews is written here too.
        _settings.RejectReusedCodes = false;
        GivenSession(TwoFactors);
        GivenFactor();
        GivenCommit(LoginCommitOutcome.ReuseAccepted);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().Equal("NEW-1", "NEW-2");
        _store.Verify(s => s.TryRegenerateCodesAsync(
            UserId, It.IsAny<SecondFactorProof>(), false, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Reused two-factor code accepted")
                    && v.ToString()!.Contains("regenerate-recovery-codes") && !v.ToString()!.Contains("123456")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _dispatched.Should().ContainSingle().Which.Should().BeOfType<TwoFactorRecoveryCodesRegeneratedEvent>();
    }

    [Theory]
    [InlineData(LoginCommitOutcome.StepReused, false, "TwoFactor.CodeAlreadyUsed")]
    [InlineData(LoginCommitOutcome.RecoveryCodesChanged, false, "TwoFactor.CodeAlreadyUsed")]
    [InlineData(LoginCommitOutcome.RecoveryCodesChanged, true, "TwoFactor.InvalidRecoveryCode")]
    [InlineData(LoginCommitOutcome.FactorLost, false, "User.TwoFactorNotEnabled")]
    public async Task Handle_CommitRefused_ShowsNoCodes_AndTellsNobody(LoginCommitOutcome outcome, bool recovery, string code)
    {
        GivenSession(TwoFactors);
        GivenFactor();
        GivenCommit(outcome);

        var result = await _handler.Handle(Command(recovery ? "RECOVERY-1" : "123456", recovery), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _dispatched.Should().BeEmpty("a change that did not happen is neither audited nor mailed");
    }
}
