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
/// X12, LK6.4–6.5: recovering an account pending deletion refuses only a live
/// administrator's lock. A lock raised by wrong passwords before the deletion
/// request is admitted: the password entry lifts it (it proved the password), the
/// provider entry leaves it (owner decision G8). Guessing is bounded by the per
/// address ceiling, before the hash, with the generic answer, and every wrong
/// password is recorded against the account.
/// </summary>
/// <remarks>Deliberate break: refuse any live lock again (<c>IsLockedOut()</c>).</remarks>
public class RecoverLockPolicyTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requests = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ILoginAttemptRepository> _attempts = new();
    private readonly Mock<ILoginResponseBuilder> _builder = new();
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
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new LoginResponse());
        _attempts
            .Setup(r => r.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<LoginAttempt, CancellationToken>((attempt, _) => _recorded.Add(attempt))
            .Returns(Task.CompletedTask);
    }

    private AccountDeletionRecoverer Recoverer() => new(
        _requests.Object, _users.Object,
        new SecondFactorVerifier(new Mock<ITwoFactorStateStore>().Object, []),
        new Mock<ITwoFactorStateStore>().Object,
        new TotpReplayPolicy(TestHelpers.CreateOptions(new Auth.Application.Configuration.TwoFactorSettings())),
        _builder.Object, new Mock<IPublisher>().Object, NullLogger<AccountDeletionRecoverer>.Instance);

    private RecoverAccountCommandHandler PasswordEntry() => new(
        _users.Object, _requests.Object, _hasher.Object, _attempts.Object, Recoverer(),
        TestHelpers.CreateOptions(TestHelpers.CreatePasswordSettings()), NullLogger<RecoverAccountCommandHandler>.Instance);

    private static RecoverAccountCommand Command() => new("owner@example.com", "the-password", null, "203.0.113.7", "Agent/1.0");

    private User GivenPendingDeletion(int count, int? minutes, bool passwordRight = true)
    {
        var deleted = TestHelpers.CreateUser(
            email: "owner@example.com", isDeleted: true, deletedAt: DateTime.UtcNow,
            status: count > 0 || minutes is not null ? UserStatus.Locked : UserStatus.Active,
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
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>()),
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
    [InlineData(null, "User.AccountLocked")]
    [InlineData(60, "User.AccountLockedUntil")]
    public async Task PasswordEntry_UnderAnAdministratorsLock_IsRefused(int? minutes, string code)
    {
        GivenPendingDeletion(0, minutes);
        if (minutes is null)
        {
            // An administrator's lock with no expiry: Locked, count 0.
            var adminLocked = TestHelpers.CreateUser(
                email: "owner@example.com", isDeleted: true, deletedAt: DateTime.UtcNow, status: UserStatus.Locked);
            _users.Setup(r => r.GetByEmailIncludeDeletedAsync("owner@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(adminLocked);
            _hasher.Setup(h => h.VerifyPassword("the-password", adminLocked.PasswordHash!)).Returns(true);
        }

        var result = await PasswordEntry().Handle(Command(), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _builder.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>()),
            Times.Never());
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

    [Theory]
    [InlineData(100, null, false, null)]
    [InlineData(0, null, true, "User.AccountLocked")]
    public async Task ProviderEntry_AdmitsAnAutomaticLockWithoutLiftingIt_AndRefusesAnAdministrators(
        int count, int? minutes, bool adminLocked, string? code)
    {
        var deleted = adminLocked
            ? TestHelpers.CreateUser(email: "owner@example.com", isDeleted: true, deletedAt: DateTime.UtcNow, status: UserStatus.Locked)
            : GivenPendingDeletion(count, minutes);
        if (adminLocked)
        {
            _users.Setup(r => r.GetByIdIncludeDeletedAsync(deleted.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deleted);
        }

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
        var handler = new RecoverAccountExternalCommandHandler(
            providers.Object, links.Object, _users.Object, _requests.Object, Recoverer(), TestHelpers.CreateExternalNonceGuard());

        var result = await handler.Handle(
            new RecoverAccountExternalCommand("google", "id-token", null, null, "203.0.113.7", "Agent/1.0", null, null),
            CancellationToken.None);

        if (code is null)
        {
            result.IsError.Should().BeFalse();
            VerifyRecovered(passwordProven: false);
        }
        else
        {
            result.FirstError.Code.Should().Be(code);
        }
    }
}
