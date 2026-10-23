using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ChangePassword;
using Auth.Application.Features.Authentication.ResetPassword;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12, LK4 exit 1 and owner decision G8 (3): a completed password reset lifts the
/// automatic lock, timed or hard, unconditionally, and so does a successful
/// change-password, which proves the current password. The statement they share
/// spares an administrator's lock (SuccessRepairSqlTests).
/// </summary>
/// <remarks>
/// Deliberate break: keep the reset's old condition (clear only a lock with an
/// expiry and a count at the threshold). The hard lock has no expiry, so the reset
/// that its own refusal names would no longer lift it: a dead end.
/// </remarks>
public class LockExitResetTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordResetTokenRepository> _tokens = new();
    private readonly Mock<IPasswordHistoryRepository> _history = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly PasswordSettings _settings = TestHelpers.CreatePasswordSettings();

    private ResetPasswordCommandHandler Reset()
    {
        var keys = new FakeCredentialHasher();
        keys.MapAll(token => $"hmac({token})");
        return new ResetPasswordCommandHandler(
            _users.Object, _tokens.Object, _history.Object, new Mock<ICredentialRevocationService>().Object,
            _hasher.Object, keys, new PasswordValidator(TestHelpers.CreateOptions(_settings)),
            TestHelpers.CreatePassingBreachEvaluator(), TestHelpers.CreateOptions(_settings),
            TestHelpers.CreateOptions(TestHelpers.CreateSessionSettings()), new Mock<IDomainEventDispatcher>().Object,
            TestHelpers.CreateAccountLockout(_users.Object, _settings), new Mock<ITwoFactorStateStore>().Object,
            NullLogger<ResetPasswordCommandHandler>.Instance);
    }

    private ChangePasswordCommandHandler Change() => new(
        _users.Object, _history.Object, new Mock<ICredentialRevocationService>().Object, _hasher.Object,
        new PasswordValidator(TestHelpers.CreateOptions(_settings)), TestHelpers.CreatePassingBreachEvaluator(),
        new Mock<IDomainEventDispatcher>().Object, TestHelpers.CreateAccountLockout(_users.Object, _settings),
        TestHelpers.CreateOptions(_settings), TestHelpers.CreateOptions(TestHelpers.CreateSessionSettings()),
        NullLogger<ChangePasswordCommandHandler>.Instance);

    private User Given(int count, int? minutes)
    {
        var user = TestHelpers.CreateUser(
            status: UserStatus.Locked, failedLoginAttempts: count,
            lockoutEnd: minutes is { } m ? DateTime.UtcNow.AddMinutes(m) : null);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("NewHash");
        _history.Setup(r => r.GetRecentHashesAsync(user.Id, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return user;
    }

    [Theory]
    [InlineData(100, null)]
    [InlineData(5, 15)]
    public async Task CompletedReset_LiftsTheAutomaticLock_HardIncluded(int count, int? minutes)
    {
        var user = Given(count, minutes);
        _tokens
            .Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf("hmac(reset-token)"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "hmac(reset-token)"));
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await Reset().Handle(new ResetPasswordCommand("reset-token", "NewPass1!"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _users.Verify(r => r.ClearAutomaticLockAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task CompletedReset_LiftsTheLockBeforeTheLinkIsSpent()
    {
        // Should lifting the lock fail, the link must still be usable for the
        // retry, or the user is left locked with a spent link (security review).
        var user = Given(100, null);
        var order = new List<string>();
        var token = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "hmac(reset-token)");
        _tokens.Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf("hmac(reset-token)"), It.IsAny<CancellationToken>())).ReturnsAsync(token);
        _tokens
            .Setup(r => r.MarkAsUsedAsync(token.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("spend"))
            .Returns(Task.CompletedTask);
        _users
            .Setup(r => r.ClearAutomaticLockAsync(user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("lift"))
            .ReturnsAsync((AccountLockTransition?)null);
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        await Reset().Handle(new ResetPasswordCommand("reset-token", "NewPass1!"), CancellationToken.None);

        order.Should().Equal("lift", "spend");
    }

    [Fact]
    public async Task SuccessfulChange_LiftsTheAutomaticLock()
    {
        var user = Given(100, null);
        _hasher.Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!)).Returns(true);
        _hasher.Setup(h => h.VerifyPassword("NewPass1!", It.IsAny<string>())).Returns(false);

        var result = await Change().Handle(new ChangePasswordCommand(user.Id, "OldPass1!", "NewPass1!"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _users.Verify(r => r.ClearAutomaticLockAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task FailedChange_LiftsNothing()
    {
        var user = Given(100, null);
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await Change().Handle(new ChangePasswordCommand(user.Id, "Wrong1!", "NewPass1!"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _users.Verify(r => r.ClearAutomaticLockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}

/// <summary>
/// X12, LK4 exit 3: an administrator's Unlock, Lock or Activate starts a new episode
/// at a count of 0, so the hard lock is lifted (Unlock, Activate) or replaced by an
/// administrator's own (Lock) and the account is not one wrong password from the
/// limit again. Deactivate leaves the count and the expiry. Held from the account's
/// decision to the statement's parameters.
/// </summary>
/// <remarks>Deliberate break: Activate without zeroing the count.</remarks>
public class LockExitAdminTests
{
    public static TheoryData<string, UserStatus, bool> Actions => new()
    {
        { "Unlock", UserStatus.Active, true },
        { "Activate", UserStatus.Active, true },
        { "Lock", UserStatus.Locked, true },
        { "Deactivate", UserStatus.Inactive, false },
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task AdministratorAction_OnAHardLockedAccount_WritesTheCountItDecided(
        string action, UserStatus status, bool resetsCount)
    {
        var user = TestHelpers.CreateUser(status: UserStatus.Locked, failedLoginAttempts: 100);
        var actor = Guid.NewGuid();
        var change = action switch
        {
            "Unlock" => user.Unlock(actor),
            "Activate" => user.Activate(actor),
            "Lock" => user.Lock(null, actor),
            _ => user.Deactivate(actor),
        };
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await LockSql.Repository(db).SetAccountStatusAsync(user.Id, change, actor, CancellationToken.None);

        var command = db.LastCommand!;
        LockSql.Normalize(command.CommandText).Should().Be(LockSql.Normalize(UserRepository.SetAccountStatusSql));
        command.Parameters["Status"].Should().Be((int)status);
        command.Parameters["ResetsLock"].Should().Be(resetsCount);
        command.Parameters["ModifiedBy"].Should().Be(actor);
        if (resetsCount)
        {
            user.FailedLoginAttempts.Should().Be(0, "the aggregate and the row agree");
        }
    }

    [Fact]
    public void Statement_ZeroesTheCountOnlyWhenTheChangeSaysSo_AndNeverOnADeletedRow()
    {
        var sql = LockSql.Normalize(UserRepository.SetAccountStatusSql);

        sql.Should().Contain("FailedLoginAttempts = CASE WHEN @ResetsLock = 1 THEN 0 ELSE FailedLoginAttempts END");
        sql.Should().Contain("LockoutEndUtc = CASE WHEN @ResetsLock = 1 THEN @LockoutEndUtc ELSE LockoutEndUtc END");
        sql.Should().EndWith("WHERE Id = @UserId AND IsDeleted = 0");
    }
}
