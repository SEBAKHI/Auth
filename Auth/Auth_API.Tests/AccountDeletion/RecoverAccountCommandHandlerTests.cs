using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
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
    private const string ProtectedSecret = "v2:protected-secret";
    private const string PlainSecret = "TESTSECRET";

    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepositoryMock = new();
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock = new();
    private readonly Mock<ITotpService> _totpServiceMock = new();
    private readonly Mock<ITwoFactorSecretProtector> _secretProtectorMock = new();
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly Mock<IPublisher> _lockoutPublisherMock = new();
    private readonly Mock<ILogger<AccountDeletionRecoverer>> _recovererLoggerMock = new();
    private readonly TwoFactorSettings _twoFactorSettings = new();
    private readonly AccountDeletionRecoverer _recoverer;
    private readonly RecoverAccountCommandHandler _handler;

    public RecoverAccountCommandHandlerTests()
    {
        _requestRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), true, null, null, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new LoginResponse());

        _recoverer = new AccountDeletionRecoverer(
            _requestRepositoryMock.Object,
            _userRepositoryMock.Object,
            new SecondFactorVerifier(
                _stateStoreMock.Object,
                [
                    new TotpProofStrategy(_totpServiceMock.Object, _secretProtectorMock.Object),
                    new RecoveryCodeProofStrategy(_totpServiceMock.Object)
                ], TestHelpers.CreateSecondFactorLockout(publisher: _lockoutPublisherMock.Object)),
            _stateStoreMock.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_twoFactorSettings)),
            _loginResponseBuilderMock.Object,
            _publisherMock.Object,
            _recovererLoggerMock.Object);

        _handler = new RecoverAccountCommandHandler(
            _userRepositoryMock.Object,
            _requestRepositoryMock.Object,
            _passwordHasherMock.Object,
            _loginAttemptRepositoryMock.Object,
            _recoverer,
            TestHelpers.CreateOptions(TestHelpers.CreatePasswordSettings()),
            new Mock<ILogger<RecoverAccountCommandHandler>>().Object);
    }

    private static RecoverAccountCommand CreateCommand(
        string email = "test@example.com", string password = "correct", string? twoFactorCode = null) =>
        new(email, password, twoFactorCode, "127.0.0.1", "TestAgent/1.0");

    private User SetupPendingDeletionUser(
        bool twoFactorEnabled = false, AccountDeletionSource source = AccountDeletionSource.InApp)
    {
        var user = TestHelpers.CreateUser(
            email: "test@example.com", isDeleted: true, deletedAt: DateTime.UtcNow,
            twoFactorEnabled: twoFactorEnabled);
        _userRepositoryMock
            .Setup(r => r.GetByEmailIncludeDeletedAsync("test@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: user.Id, email: "test@example.com"));
        _requestRepositoryMock
            .Setup(r => r.GetActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountDeletionRequest.Create(
                user.Id, source, TimeSpan.FromDays(30), "2026.07", user.Id));
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

    /// <summary>
    /// The factor row as the recovery reads it, an attempt to spare, and a code
    /// that checks out against its secret.
    /// </summary>
    private void GivenFactor(Guid userId, bool isEnabled, DateTime? lockedUntil = null)
    {
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(userId, ProtectedSecret, "[]", isEnabled, failedAttempts: 0, lockedUntil));
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _secretProtectorMock
            .Setup(p => p.UnprotectAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlainSecret);
        _totpServiceMock.Setup(s => s.ValidateCode(PlainSecret, "123456")).Returns(MatchedStep);
    }

    [Fact]
    public async Task Handle_TwoFactorEnabledWithValidCode_Recovers()
    {
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        GivenFactor(user.Id, isEnabled: false);

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
        GivenFactor(user.Id, isEnabled: true);
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
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>()),
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
        GivenFactor(user.Id, isEnabled: false);

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
    public async Task Handle_ReuseAcceptedWithTheSwitchOff_RecoversAndLogsTheReuse()
    {
        // TwoFactor:RejectReusedCodes off: the store let the reused step settle.
        // Turning the switch off must relax recovery, never break it, so the
        // account is restored — and the reuse is logged for the operator. The
        // switch is read per request, not when the recoverer was built.
        _twoFactorSettings.RejectReusedCodes = false;
        var user = GivenEnabledFactorAndValidCode(LoginCommitOutcome.Committed);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(user.Id, MatchedStep, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.ReuseAccepted);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _recovererLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Reused two-factor code accepted (RejectReusedCodes=false)")
                    && v.ToString()!.Contains("account-recovery")),
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

    // ── T6 / OI-30: the recovery checks a code like every second factor ─────

    [Fact]
    public async Task Recover_Locked_ReturnsLockedOut_WithoutVerifying()
    {
        // Someone holding the password of an account pending deletion used to be
        // able to guess its TOTP code here at the rate limit alone. The attempt is
        // now reserved first: a locked factor checks nothing and restores nothing.
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        GivenFactor(user.Id, isEnabled: true, lockedUntil: DateTime.UtcNow.AddMinutes(10));

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        _totpServiceMock.Verify(s => s.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _secretProtectorMock.Verify(
            p => p.UnprotectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingRecovered();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // the flag says on, the row is pending: the pending secret is the factor
    public async Task Recover_InAppRequest_FactorAtTheLimit_IsNotCapped_TheCodeIsCheckedUnderTheTimedLock(bool rowEnabled)
    {
        // X12 PR B, owner decisions on the row «pending deletion»: (b), narrowed by
        // D-66-1 (a). No way out of the hard lock reaches an account waiting for
        // deletion, so a deletion its own signed-in session asked for recovers under
        // the timed lock alone. The attempt is still counted first, with no limit,
        // and the code is checked; reaching the limit here reports nothing.
        // Deliberate break: never exempt.
        var user = SetupPendingDeletionUser(twoFactorEnabled: true, source: AccountDeletionSource.InApp);
        GivenFactor(user.Id, isEnabled: rowEnabled);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(user.Id, ProtectedSecret, "[]", rowEnabled, failedAttempts: 99, lockedUntil: null));
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "654321"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code, "the wrong code was checked and refused as wrong");
        _stateStoreMock.Verify(
            s => s.TryReserveAttemptAsync(user.Id, (int?)null, It.IsAny<CancellationToken>()), Times.Once);
        _totpServiceMock.Verify(s => s.ValidateCode(PlainSecret, "654321"), Times.Once);
        _lockoutPublisherMock.Verify(
            p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()), Times.Never,
            "the hundredth wrong code on this path is no hard lock");
        VerifyNothingRecovered();
    }

    [Fact]
    public async Task Recover_InAppRequest_FactorPastTheLimit_TheRightCodeStillRestoresTheAccount()
    {
        // The pending row: accepted without a step claim, as before (the flags disagree).
        var user = SetupPendingDeletionUser(twoFactorEnabled: true, source: AccountDeletionSource.InApp);
        GivenFactor(user.Id, isEnabled: false);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(user.Id, ProtectedSecret, "[]", false, failedAttempts: 120, lockedUntil: null));

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <remarks>
    /// D-66-1 (a): a deletion asked through the public no-login page proved the
    /// mailbox alone, so the mailbox and the password together would otherwise buy
    /// about 2,885 code guesses on demand. It recovers under the limit, refused
    /// visibly like every door. Deliberate break: always exempt.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recover_PublicWebRequest_FactorAtTheLimit_IsRefusedLockedAdminResetRequired_WithoutVerifying(bool rowEnabled)
    {
        var user = SetupPendingDeletionUser(twoFactorEnabled: true, source: AccountDeletionSource.PublicWeb);
        GivenFactor(user.Id, isEnabled: rowEnabled);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(user.Id, ProtectedSecret, "[]", rowEnabled, failedAttempts: 100, lockedUntil: null));

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedAdminResetRequired.Code, "shown as it is, never masked");
        _stateStoreMock.Verify(
            s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _totpServiceMock.Verify(s => s.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingRecovered();
    }

    [Fact]
    public async Task RecoverExternal_PublicWebRequest_FactorAtTheLimit_IsRefusedTheSameWay()
    {
        // The provider entry goes through the same recoverer, so the source decides
        // there too.
        var user = SetupPendingDeletionUser(twoFactorEnabled: true, source: AccountDeletionSource.PublicWeb);
        GivenFactor(user.Id, isEnabled: true);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(user.Id, ProtectedSecret, "[]", true, failedAttempts: 100, lockedUntil: null));
        var provider = new Mock<IExternalAuthProvider>();
        provider
            .Setup(p => p.ValidateTokenAsync("id-token", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo("provider-user", "test@example.com", "Test", "User", "Test User", null, true));
        var providers = new Mock<IExternalAuthProviderFactory>();
        providers.Setup(p => p.GetProvider("google")).Returns(provider.Object);
        var links = new Mock<IUserExternalLoginRepository>();
        links
            .Setup(r => r.GetByProviderAsync("google", "provider-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserExternalLogin(userId: user.Id, provider: "google", providerUserId: "provider-user"));
        var external = new RecoverAccountExternalCommandHandler(
            providers.Object, links.Object, _userRepositoryMock.Object, _requestRepositoryMock.Object,
            _recoverer, TestHelpers.CreateExternalNonceGuard());

        var result = await external.Handle(
            new RecoverAccountExternalCommand("google", "id-token", null, "123456", "203.0.113.7", "Agent/1.0"),
            CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedAdminResetRequired.Code);
        _stateStoreMock.Verify(
            s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingRecovered();
    }

    [Theory]
    [InlineData(AccountDeletionSource.PublicWeb, 1)]
    [InlineData(AccountDeletionSource.InApp, 0)]
    public async Task Recover_TheReservationThatReachesTheLimit_IsATransition_OnlyUnderTheCap(
        AccountDeletionSource source, int transitions)
    {
        var user = SetupPendingDeletionUser(twoFactorEnabled: true, source: source);
        GivenFactor(user.Id, isEnabled: true);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(user.Id, ProtectedSecret, "[]", true, failedAttempts: 99, lockedUntil: null));
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "654321"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code, "the hundredth wrong code is still checked");
        _lockoutPublisherMock.Verify(
            p => p.Publish(It.Is<UserHardLockedEvent>(e => e.UserId == user.Id && e.Factor == HardLockedFactor.SecondFactor), It.IsAny<CancellationToken>()),
            Times.Exactly(transitions));
        VerifyNothingRecovered();
    }

    [Fact]
    public async Task Recover_ReservationRefused_ReturnsLockedOut_WithoutVerifying()
    {
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        GivenFactor(user.Id, isEnabled: true);
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        _totpServiceMock.Verify(s => s.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingRecovered();
    }

    [Fact]
    public async Task Recover_WrongCode_IsCountedBeforeTheCheck_AndRestoresNothing()
    {
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        GivenFactor(user.Id, isEnabled: true);
        var order = new List<string>();
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("reserve"))
            .ReturnsAsync(1);
        _totpServiceMock
            .Setup(s => s.ValidateCode(PlainSecret, "000000"))
            .Callback(() => order.Add("verify"))
            .Returns((long?)null);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "000000"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code);
        order.Should().Equal("reserve", "verify");
        _stateStoreMock.Verify(
            s => s.TryClaimTotpStepAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "a wrong code settles nothing, so the failure stays counted");
        VerifyNothingRecovered();
    }

    [Fact]
    public async Task Recover_PendingRow_KeepsX01Rule()
    {
        // The account flag is on but the factor row is only pending (the two
        // disagree). The code is checked against the pending secret — after an
        // attempt was reserved on that row — and accepted without a step claim,
        // with the warning; otherwise the account could never be recovered.
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        GivenFactor(user.Id, isEnabled: false);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        _secretProtectorMock.Verify(
            p => p.UnprotectAsync(user.Id, ProtectedSecret, It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Recover_NoFactorRow_ReturnsInvalidTwoFactorCode()
    {
        // The flag says on and there is no row at all: the answer a missing factor
        // has always had here, and nothing is checked or reserved.
        var user = SetupPendingDeletionUser(twoFactorEnabled: true);
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwoFactorSnapshot?)null);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code);
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingRecovered();
    }
}
