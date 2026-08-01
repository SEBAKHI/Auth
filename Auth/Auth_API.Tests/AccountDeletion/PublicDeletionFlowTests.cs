using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.AccountDeletion.ConfirmPublicDeletion;
using Auth.Application.Features.AccountDeletion.PublicRequestDeletion;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// Unit tests for the public no-login deletion flow: absolute anti-enumeration
/// on the request step, generic OTP errors and idempotent confirmation.
/// </summary>
public class PublicDeletionFlowTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<IAccountDeletionVerificationRepository> _verificationRepositoryMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IOtpGenerator> _otpGeneratorMock = new();
    private readonly DeletionOtpService _otpService;
    private readonly AccountDeletionRequestor _requestor;

    public PublicDeletionFlowTests()
    {
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());
        _requestRepositoryMock
            .Setup(r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _otpGeneratorMock.Setup(g => g.GenerateNumericOtp(6)).Returns("123456");
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        var settings = TestHelpers.CreateOptions(new AccountDeletionSettings());
        _otpService = new DeletionOtpService(
            _verificationRepositoryMock.Object,
            _notificationServiceMock.Object,
            _otpGeneratorMock.Object,
            _passwordHasherMock.Object,
            settings,
            TestHelpers.CreateOptions(new EmailSettings()),
            new Mock<ILogger<DeletionOtpService>>().Object);
        _requestor = new AccountDeletionRequestor(
            _requestRepositoryMock.Object,
            _userRepositoryMock.Object,
            new OwnedOrganizationDeletionGuard(
                _organizationRepositoryMock.Object,
                new Mock<ILogger<OwnedOrganizationDeletionGuard>>().Object),
            new Mock<ICredentialRevocationService>().Object,
            new Mock<IPublisher>().Object,
            settings,
            new Mock<ILogger<AccountDeletionRequestor>>().Object);
    }

    private PublicRequestDeletionCommandHandler CreateRequestHandler() => new(
        _userRepositoryMock.Object, _otpService,
        new Mock<ILogger<PublicRequestDeletionCommandHandler>>().Object);

    private ConfirmPublicDeletionCommandHandler CreateConfirmHandler() => new(
        _userRepositoryMock.Object, _requestRepositoryMock.Object, _otpService, _requestor);

    [Fact]
    public async Task Request_UnknownEmail_AcknowledgesGenericallyWithoutSendingAnything()
    {
        var result = await CreateRequestHandler().Handle(
            new PublicRequestDeletionCommand("nobody@example.com"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _notificationServiceMock.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Request_KnownEmail_SendsOtpWithSameGenericAcknowledgment()
    {
        var user = TestHelpers.CreateUser(email: "known@example.com");
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("known@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await CreateRequestHandler().Handle(
            new PublicRequestDeletionCommand("known@example.com"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _verificationRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<AccountDeletionVerification>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationServiceMock.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Request_RateLimited_StillAcknowledgesGenerically()
    {
        var user = TestHelpers.CreateUser(email: "known@example.com");
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("known@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _verificationRepositoryMock
            .Setup(r => r.GetRecentCountAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(int.MaxValue);

        var result = await CreateRequestHandler().Handle(
            new PublicRequestDeletionCommand("known@example.com"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _notificationServiceMock.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("unknownEmail")]
    [InlineData("wrongCode")]
    public async Task Confirm_EveryOtpFailureShape_ReturnsIdenticalInvalidOtp(string shape)
    {
        if (shape == "wrongCode")
        {
            var verification = AccountDeletionVerification.Create(Guid.NewGuid(), "a@b.com", "otp-hash");
            _verificationRepositoryMock
                .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(verification);
            _passwordHasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        }

        var result = await CreateConfirmHandler().Handle(
            new ConfirmPublicDeletionCommand("a@b.com", "000000"), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.InvalidOtp);
    }

    [Fact]
    public async Task Confirm_ValidOtp_SchedulesDeletionViaTheSharedPipeline()
    {
        var user = TestHelpers.CreateUser(email: "a@b.com");
        var verification = AccountDeletionVerification.Create(user.Id, "a@b.com", "otp-hash");
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        _passwordHasherMock.Setup(h => h.VerifyPassword("123456", "otp-hash")).Returns(true);
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await CreateConfirmHandler().Handle(
            new ConfirmPublicDeletionCommand("a@b.com", "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _requestRepositoryMock.Verify(
            r => r.TryCreateAsync(
                It.Is<AccountDeletionRequest>(req => req.Source == AccountDeletionSource.PublicWeb),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Confirm_AccountAlreadyPendingDeletion_IsIdempotentSuccess()
    {
        var user = TestHelpers.CreateUser(email: "a@b.com", isDeleted: true, deletedAt: DateTime.UtcNow);
        var verification = AccountDeletionVerification.Create(user.Id, "a@b.com", "otp-hash");
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        _passwordHasherMock.Setup(h => h.VerifyPassword("123456", "otp-hash")).Returns(true);
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _requestRepositoryMock
            .Setup(r => r.GetActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountDeletionRequest.Create(
                user.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", user.Id));

        var result = await CreateConfirmHandler().Handle(
            new ConfirmPublicDeletionCommand("a@b.com", "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _requestRepositoryMock.Verify(
            r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
