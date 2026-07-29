using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.AccountDeletion.RequestAccountDeletion;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// Unit tests for the in-app deletion request: mandatory fresh
/// re-authentication (password or OTP) before the shared pipeline runs.
/// </summary>
public class RequestAccountDeletionCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IAccountDeletionVerificationRepository> _verificationRepositoryMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly RequestAccountDeletionCommandHandler _handler;

    public RequestAccountDeletionCommandHandlerTests()
    {
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());
        _requestRepositoryMock
            .Setup(r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = Options.Create(new AccountDeletionSettings());
        var requestor = new AccountDeletionRequestor(
            _requestRepositoryMock.Object,
            _userRepositoryMock.Object,
            new OwnedOrganizationDeletionGuard(
                _organizationRepositoryMock.Object,
                new Mock<ILogger<OwnedOrganizationDeletionGuard>>().Object),
            _credentialRevocationMock.Object,
            new Mock<IPublisher>().Object,
            settings,
            new Mock<ILogger<AccountDeletionRequestor>>().Object);
        var otpService = new DeletionOtpService(
            _verificationRepositoryMock.Object,
            _notificationServiceMock.Object,
            new Mock<IOtpGenerator>().Object,
            _passwordHasherMock.Object,
            settings,
            Options.Create(new EmailSettings()),
            new Mock<ILogger<DeletionOtpService>>().Object);

        _handler = new RequestAccountDeletionCommandHandler(
            _userRepositoryMock.Object,
            _passwordHasherMock.Object,
            otpService,
            requestor);
    }

    [Fact]
    public async Task Handle_CorrectPassword_SchedulesDeletionAndReturnsGraceDeadline()
    {
        var user = TestHelpers.CreateUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.VerifyPassword("correct", user.PasswordHash!)).Returns(true);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, "correct", null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.GraceEndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        _userRepositoryMock.Verify(r => r.DeleteAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WrongPassword_RefusesWithoutSideEffects()
    {
        var user = TestHelpers.CreateUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, "wrong", null), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidCurrentPassword);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PasswordAccountWithoutPassword_RefusesEvenWithOtp()
    {
        // A stolen session must not sidestep the password by supplying an OTP.
        var user = TestHelpers.CreateUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, null, "123456"), CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidCurrentPassword);
    }

    [Fact]
    public async Task Handle_PasswordlessAccountWithValidOtp_SchedulesDeletion()
    {
        var user = CreatePasswordlessUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var verification = AccountDeletionVerification.Create(user.Id, user.Email, "otp-hash");
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        _passwordHasherMock.Setup(h => h.VerifyPassword("123456", "otp-hash")).Returns(true);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, null, "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _verificationRepositoryMock.Verify(r => r.MarkAsUsedAsync(verification.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PasswordlessAccountWithWrongOtp_RefusesGenerically()
    {
        var user = CreatePasswordlessUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var verification = AccountDeletionVerification.Create(user.Id, user.Email, "otp-hash");
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        _passwordHasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, null, "000000"), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.InvalidOtp);
        _verificationRepositoryMock.Verify(
            r => r.IncrementAttemptCountAsync(verification.Id, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static User CreatePasswordlessUser()
    {
        var id = Guid.NewGuid();
        return new User(
            id: id, email: $"external-{id:N}@test.com", normalizedEmail: $"EXTERNAL-{id:N}@TEST.COM",
            passwordHash: null, firstName: "Ext", lastName: "User", displayName: null, phoneNumber: null,
            status: Auth.Domain.Enums.UserStatus.Active, emailConfirmed: true, phoneConfirmed: false,
            twoFactorEnabled: false, twoFactorSecret: null, failedLoginAttempts: 0, lockoutEnd: null,
            lastLoginAt: null, passwordChangedAt: null, mustChangePassword: false,
            preferredLanguage: "en", timeZone: "UTC", metadata: null, isSystemUser: false,
            createdAt: DateTime.UtcNow, createdBy: Guid.NewGuid(), modifiedAt: null, modifiedBy: null);
    }
}
