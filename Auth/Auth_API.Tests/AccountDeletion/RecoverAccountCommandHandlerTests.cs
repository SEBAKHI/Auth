using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// Unit tests for password-based grace-period recovery: the anti-enumeration
/// equalities, the deterministic cancel-vs-claim race, the 2FA gate and the
/// restore + auto-login path.
/// </summary>
public class RecoverAccountCommandHandlerTests
{
    private const long MatchedStep = 59_313_872;

    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorAuthRepositoryMock = new();
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock = new();
    private readonly Mock<ITotpService> _totpServiceMock = new();
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<ILogger<AccountDeletionRecoverer>> _recovererLoggerMock = new();
    private readonly RecoverAccountCommandHandler _handler;

    public RecoverAccountCommandHandlerTests()
    {
        _requestRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), true, null, null))
            .ReturnsAsync(new LoginResponse());

        _handler = new RecoverAccountCommandHandler(
            _userRepositoryMock.Object,
            _requestRepositoryMock.Object,
            _passwordHasherMock.Object,
            new AccountDeletionRecoverer(
                _requestRepositoryMock.Object,
                _userRepositoryMock.Object,
                _twoFactorAuthRepositoryMock.Object,
                _stateStoreMock.Object,
                _totpServiceMock.Object,
                new TotpReplayPolicy(TestHelpers.CreateOptions(new TwoFactorSettings())),
                _loginResponseBuilderMock.Object,
                _publisherMock.Object,
                _recovererLoggerMock.Object),
            new Mock<ILogger<RecoverAccountCommandHandler>>().Object);
    }

    private static RecoverAccountCommand CreateCommand(
        string email = "test@example.com", string password = "correct", string? twoFactorCode = null) =>
        new(email, password, twoFactorCode, "127.0.0.1", "TestAgent/1.0");

    private User SetupPendingDeletionUser(bool twoFactorEnabled = false)
    {
        var user = TestHelpers.CreateUser(
            email: "test@example.com", isDeleted: true, deletedAt: DateTime.UtcNow,
            twoFactorEnabled: twoFactorEnabled);
        _userRepositoryMock
            .Setup(r => r.GetByEmailIncludeDeletedAsync("test@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: user.Id, email: "test@example.com"));
        _requestRepositoryMock
            .Setup(r => r.GetActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountDeletionRequest.Create(
                user.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", user.Id));
        _passwordHasherMock.Setup(h => h.VerifyPassword("correct", user.PasswordHash!)).Returns(true);
        return user;
    }

    [Fact]
    public async Task Handle_ValidCredentials_CancelsRestoresAndSignsIn()
    {
        var user = SetupPendingDeletionUser();

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _requestRepositoryMock.Verify(
            r => r.UpdateAsync(
                It.Is<AccountDeletionRequest>(req => req.Status == AccountDeletionStatus.Cancelled),
                AccountDeletionStatus.PendingGrace,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(r => r.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<AccountDeletionCancelledEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("unknown")]     // no such account
    [InlineData("live")]        // account exists and is not deleted
    [InlineData("adminDeleted")] // soft-deleted by an admin: no request row
    [InlineData("wrongPassword")]
    public async Task Handle_EveryNonRecoverableShape_ReturnsIdenticalInvalidCredentials(string shape)
    {
        switch (shape)
        {
            case "live":
                _userRepositoryMock
                    .Setup(r => r.GetByEmailIncludeDeletedAsync("test@example.com", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateUser(email: "test@example.com"));
                break;
            case "adminDeleted":
                var adminDeleted = TestHelpers.CreateUser(
                    email: "test@example.com", isDeleted: true, deletedAt: DateTime.UtcNow);
                _userRepositoryMock
                    .Setup(r => r.GetByEmailIncludeDeletedAsync("test@example.com", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(adminDeleted);
                _passwordHasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
                break;
            case "wrongPassword":
                SetupPendingDeletionUser();
                _passwordHasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
                break;
        }

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidCredentials);
        _userRepositoryMock.Verify(r => r.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LostClaimRace_ReturnsRecoveryWindowExpired()
    {
        SetupPendingDeletionUser();
        _requestRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.RecoveryWindowExpired);
        _userRepositoryMock.Verify(r => r.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TwoFactorEnabledWithoutCode_RequiresTwoFactor()
    {
        SetupPendingDeletionUser(twoFactorEnabled: true);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.TwoFactorRequired);
        _userRepositoryMock.Verify(r => r.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TwoFactorEnabledWithValidCode_Recovers()
    {
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        _twoFactorAuthRepositoryMock
            .Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateTwoFactorAuth(userId: user.Id));
        _totpServiceMock.Setup(s => s.ValidateCode(It.IsAny<string>(), "123456")).Returns(MatchedStep);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The step claim: a code counts once ─────────────────────────────────

    /// <summary>
    /// A user with two-factor on, an ENABLED factor row, a code that checks out,
    /// and the step claim's answer — stated, because a loose mock's zero would
    /// read as a refusal.
    /// </summary>
    private User GivenEnabledFactorAndValidCode(LoginCommitOutcome claim)
    {
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        _twoFactorAuthRepositoryMock
            .Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateTwoFactorAuth(userId: user.Id, isEnabled: true));
        _totpServiceMock.Setup(s => s.ValidateCode(It.IsAny<string>(), "123456")).Returns(MatchedStep);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(user.Id, MatchedStep, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);
        return user;
    }

    private void VerifyNothingRecovered()
    {
        _requestRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userRepositoryMock.Verify(r => r.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<AccountDeletionCancelledEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task ReusedStep_ReturnsCodeAlreadyUsed_AndDoesNotRestore()
    {
        // The code the owner just used, presented again with the leaked password
        // to cancel a pending deletion. The claim runs before the request is
        // cancelled, so nothing is restored and no session is issued.
        var user = GivenEnabledFactorAndValidCode(LoginCommitOutcome.StepReused);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.CodeAlreadyUsed");
        _stateStoreMock.Verify(
            s => s.TryClaimTotpStepAsync(user.Id, MatchedStep, true, It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyNothingRecovered();
        _recovererLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Reused two-factor code rejected")
                    && v.ToString()!.Contains("account-recovery")
                    && !v.ToString()!.Contains("123456")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_FirstUseOfTheCode_ClaimsTheStepAndRecovers()
    {
        var user = GivenEnabledFactorAndValidCode(LoginCommitOutcome.Committed);
        var order = new List<string>();
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(user.Id, MatchedStep, true, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("claim"))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        _requestRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("cancel"))
            .ReturnsAsync(true);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        order.Should().Equal("claim", "cancel");
        _userRepositoryMock.Verify(r => r.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FlagsDisagree_RecoversWithoutAStepClaim()
    {
        // The account says two-factor is on but its factor row is not enabled. The
        // claim settles only an enabled factor, so claiming here would refuse every
        // code and leave the account unrecoverable. The code is checked as before
        // and accepted without a claim, with a warning about the disagreement.
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        _twoFactorAuthRepositoryMock
            .Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateTwoFactorAuth(userId: user.Id, isEnabled: false));
        _totpServiceMock.Setup(s => s.ValidateCode(It.IsAny<string>(), "123456")).Returns(MatchedStep);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _stateStoreMock.Verify(
            s => s.TryClaimTotpStepAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userRepositoryMock.Verify(r => r.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _recovererLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("without a step claim")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_FactorGoneUnderTheClaim_ReturnsInvalidTwoFactorCode()
    {
        GivenEnabledFactorAndValidCode(LoginCommitOutcome.FactorLost);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidTwoFactorCode");
        VerifyNothingRecovered();
    }

    [Fact]
    public async Task Handle_StepClaimFaults_PropagatesAndRestoresNothing()
    {
        // Fail-closed: a claim the database could not run — a missing column after
        // a stale schema publish, a timeout — is never taken for an accepted code.
        // The fault reaches the central handler; no account is restored and no
        // token is issued.
        var user = GivenEnabledFactorAndValidCode(LoginCommitOutcome.Committed);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(user.Id, MatchedStep, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid column name 'LastUsedTimeStep'."));

        var act = () => _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyNothingRecovered();
    }
}
