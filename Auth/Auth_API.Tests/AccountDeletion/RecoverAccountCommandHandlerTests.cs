using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
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
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorAuthRepositoryMock = new();
    private readonly Mock<ITotpService> _totpServiceMock = new();
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
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
                _totpServiceMock.Object,
                _loginResponseBuilderMock.Object,
                _publisherMock.Object,
                new Mock<ILogger<AccountDeletionRecoverer>>().Object),
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
        _totpServiceMock.Setup(s => s.ValidateCode(It.IsAny<string>(), "123456")).Returns(true);

        var result = await _handler.Handle(CreateCommand(twoFactorCode: "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
