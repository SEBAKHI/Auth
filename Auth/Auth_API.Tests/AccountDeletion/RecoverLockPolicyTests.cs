using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// X12, LK6.4–6.5: no lock refuses the recovery of an account pending deletion. A lock
/// raised by wrong passwords before the deletion request is admitted: the password entry
/// lifts it (it proved the password), the provider entry leaves it (owner decision G8). An
/// administrator's lock, and a deactivated or not yet activated account, have the deletion
/// cancelled and get no session (REVIEW 151 F1, D-151-1 (b)): the account keeps its state.
/// Guessing is bounded by the per address ceiling, before the hash, with the generic answer,
/// and every wrong password is recorded against the account.
/// </summary>
/// <remarks>
/// Deliberate breaks: refuse any live lock again (<c>IsLockedOut()</c>); the recoverer's state
/// check removed (a session is minted); an administrator's lock refused at the password door
/// again (nothing is cancelled).
/// </remarks>
public class RecoverLockPolicyTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requests = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ILoginAttemptRepository> _attempts = new();
    private readonly Mock<ILoginResponseBuilder> _builder = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly List<LoginAttempt> _recorded = [];

    public RecoverLockPolicyTests()
    {
        _requests
            .Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _builder
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new LoginResponse());
        _attempts
            .Setup(r => r.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<LoginAttempt, CancellationToken>((attempt, _) => _recorded.Add(attempt))
            .Returns(Task.CompletedTask);
    }

    private AccountDeletionRecoverer Recoverer() => new(
        _requests.Object, _users.Object,
        new SecondFactorVerifier(new Mock<ITwoFactorStateStore>().Object, [], TestHelpers.CreateSecondFactorLockout()),
        new Mock<ITwoFactorStateStore>().Object,
        new Mock<ITwoFactorChallengeRepository>().Object,
        new Mock<IEmailCodeSender>().Object,
        new FakeCredentialHasher(),
        new Mock<IJwtTokenService>().Object,
        TestHelpers.CreateSecondFactorLockout(),
        _builder.Object, TestHelpers.PasskeyOnlyAdministratorRuleOff(), _publisher.Object, NullLogger<AccountDeletionRecoverer>.Instance);

    private RecoverAccountCommandHandler PasswordEntry() => new(
        _users.Object, _requests.Object, _hasher.Object, _attempts.Object, Recoverer(),
        TestHelpers.CreateOptions(TestHelpers.CreatePasswordSettings()), NullLogger<RecoverAccountCommandHandler>.Instance);

    private static RecoverAccountCommand Command() => new("owner@example.com", "the-password", null, "203.0.113.7", "Agent/1.0");

    private User GivenPendingDeletion(int count, int? minutes, bool passwordRight = true, UserStatus? status = null)
    {
        var deleted = TestHelpers.CreateUser(
            email: "owner@example.com", isDeleted: true, deletedAt: DateTime.UtcNow,
            status: status ?? (count > 0 || minutes is not null ? UserStatus.Locked : UserStatus.Active),
            failedLoginAttempts: count,
            lockoutEnd: minutes is { } m ? DateTime.UtcNow.AddMinutes(m) : null);
        _users.Setup(r => r.GetByEmailIncludeDeletedAsync("owner@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(deleted);
        _users.Setup(r => r.GetByIdIncludeDeletedAsync(deleted.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deleted);
        _users.Setup(r => r.GetByIdAsync(deleted.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deleted);
        _requests
            .Setup(r => r.GetActiveByUserIdAsync(deleted.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountDeletionRequest.Create(deleted.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", deleted.Id));
        _hasher.Setup(h => h.VerifyPassword("the-password", deleted.PasswordHash!)).Returns(passwordRight);
        return deleted;
    }

    private void VerifyRecovered(bool passwordProven) =>
        _builder.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), passwordProven, It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>()),
            Times.Once());

    [Theory]
    [InlineData(5, 15)]
    [InlineData(100, null)]
    public async Task PasswordEntry_UnderAnAutomaticLock_RecoversAndLiftsIt(int count, int? minutes)
    {
        GivenPendingDeletion(count, minutes);

        var result = await PasswordEntry().Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyRecovered(passwordProven: true);
    }

    [Theory]
    [MemberData(nameof(DeletionCancelledAssertions.StatesThatRefuseASession), MemberType = typeof(DeletionCancelledAssertions))]
    public async Task PasswordEntry_AStateThatRefusesASession_CancelsTheDeletion_AndIssuesNoSession(
        string because, UserStatus status, int count, int? minutes, string code)
    {
        // REVIEW 151 F1, D-151-1 (b): an administrator's lock no longer refuses at the door, so
        // the account that asked to stay is not deleted at the end of its grace period.
        var deleted = GivenPendingDeletion(count, minutes, status: status);

        var result = await PasswordEntry().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(code, because);
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Forbidden, because);
        DeletionCancelledAssertions.VerifyCancelledWithoutASession(_requests, _users, _builder, _publisher, deleted.Id, because);
        _recorded.Should().BeEmpty($"{because}: the password was right, and no sign-in row is written");
    }

    [Fact]
    public async Task PasswordEntry_WrongPassword_IsRecordedAgainstTheAccount()
    {
        var deleted = GivenPendingDeletion(0, null, passwordRight: false);

        var result = await PasswordEntry().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _recorded.Should().ContainSingle(a => a.UserId == deleted.Id && a.FailureReason == LoginFailureReasons.InvalidPassword);
    }

    [Fact]
    public async Task PasswordEntry_CeilingReached_RefusesBeforeTheHash()
    {
        var deleted = GivenPendingDeletion(0, null);
        _attempts
            .Setup(r => r.CountFailedAttemptsForUserFromIpAsync(deleted.Id, "203.0.113.7", TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await PasswordEntry().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        _recorded.Should().ContainSingle(a => a.UserId == deleted.Id && a.FailureReason == LoginFailureReasons.SourceLocked);
    }

    [Fact]
    public async Task PasswordEntry_CeilingReachedDuringTheHash_DoesNotRecover()
    {
        // Review 64 F2: a recovery restores the account and signs it in, so the
        // correct guess of a burst that passed the ceiling together is counted
        // again after the hash.
        var deleted = GivenPendingDeletion(0, null);
        _attempts
            .SetupSequence(r => r.CountFailedAttemptsForUserFromIpAsync(deleted.Id, "203.0.113.7", TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4)
            .ReturnsAsync(5);

        var result = await PasswordEntry().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _requests.Verify(
            r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _builder.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>()),
            Times.Never());
        _recorded.Should().ContainSingle(a => a.UserId == deleted.Id && a.FailureReason == LoginFailureReasons.SourceLocked);
    }

    [Fact]
    public async Task PasswordEntry_UnderLegacy_HasNoCeiling()
    {
        // Review 64 F9: Legacy's third effect (LK9.2 (iii)) on this entry too.
        var settings = TestHelpers.CreatePasswordSettings();
        settings.ConsecutiveFailureMode = Auth.Application.Configuration.ConsecutiveFailureMode.Legacy;
        var deleted = GivenPendingDeletion(0, null);
        _attempts
            .Setup(r => r.CountFailedAttemptsForUserFromIpAsync(deleted.Id, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);
        var handler = new RecoverAccountCommandHandler(
            _users.Object, _requests.Object, _hasher.Object, _attempts.Object, Recoverer(),
            TestHelpers.CreateOptions(settings), NullLogger<RecoverAccountCommandHandler>.Instance);

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _attempts.Verify(
            r => r.CountFailedAttemptsForUserFromIpAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task ProviderEntry_AdmitsAnAutomaticLockWithoutLiftingIt()
    {
        var deleted = GivenPendingDeletion(100, null);

        var result = await ProviderEntry(deleted).Handle(ProviderCommand(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyRecovered(passwordProven: false);
    }

    [Theory]
    [MemberData(nameof(DeletionCancelledAssertions.StatesThatRefuseASession), MemberType = typeof(DeletionCancelledAssertions))]
    public async Task ProviderEntry_AStateThatRefusesASession_CancelsTheDeletion_AndIssuesNoSession(
        string because, UserStatus status, int count, int? minutes, string code)
    {
        var deleted = GivenPendingDeletion(count, minutes, status: status);

        var result = await ProviderEntry(deleted).Handle(ProviderCommand(), CancellationToken.None);

        result.FirstError.Code.Should().Be(code, because);
        DeletionCancelledAssertions.VerifyCancelledWithoutASession(_requests, _users, _builder, _publisher, deleted.Id, because);
    }

    private static RecoverAccountExternalCommand ProviderCommand() =>
        new("google", "id-token", null, null, "203.0.113.7", "Agent/1.0", null, null);

    private RecoverAccountExternalCommandHandler ProviderEntry(User deleted)
    {
        var provider = new Mock<IExternalAuthProvider>();
        provider
            .Setup(p => p.ValidateTokenAsync("id-token", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalUserInfo("provider-user", "owner@example.com", "Owner", "User", "Owner User", null, true));
        var providers = new Mock<IExternalAuthProviderFactory>();
        providers.Setup(f => f.GetProvider("google")).Returns(provider.Object);
        var links = new Mock<IUserExternalLoginRepository>();
        links
            .Setup(r => r.GetByProviderAsync("google", "provider-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserExternalLogin(userId: deleted.Id, provider: "google", providerUserId: "provider-user"));
        return new RecoverAccountExternalCommandHandler(
            providers.Object, links.Object, _users.Object, _requests.Object, Recoverer(), TestHelpers.CreateExternalNonceGuard());
    }
}
