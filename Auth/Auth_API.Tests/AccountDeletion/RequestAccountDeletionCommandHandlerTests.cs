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
/// re-authentication by emailed code — the same single factor for every
/// account, whether or not it has a password.
/// </summary>
public class RequestAccountDeletionCommandHandlerTests
{
    private const string OtpHash = "otp-hash";
    private const string ValidOtp = "123456";

    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IAccountDeletionVerificationRepository> _verificationRepositoryMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IOtpHasher> _otpHasherMock = new();
    private readonly RequestAccountDeletionCommandHandler _handler;

    public RequestAccountDeletionCommandHandlerTests()
    {
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());
        _requestRepositoryMock
            .Setup(r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = TestHelpers.CreateOptions(new AccountDeletionSettings());
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
            _otpHasherMock.Object,
            settings,
            TestHelpers.CreateOptions(new EmailSettings()),
            new Mock<IEnvironmentInfo>().Object,
            new Mock<ILogger<DeletionOtpService>>().Object);

        _handler = new RequestAccountDeletionCommandHandler(
            _userRepositoryMock.Object, otpService, requestor);
    }

    [Fact]
    public async Task Handle_ValidOtp_SchedulesDeletionAndReturnsGraceDeadline()
    {
        var user = TestHelpers.CreateUser(passwordHash: null);
        var verification = ArrangeOutstandingCode(user, matches: true);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, ValidOtp), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.GraceEndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        _userRepositoryMock.Verify(r => r.DeleteAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _verificationRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(verification.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AccountWithPassword_ConfirmsWithTheEmailedCodeAndIsNeverAskedForItsPassword()
    {
        // The whole point of the unified flow: holding a password no longer
        // changes the factor, so an external-only account and a password
        // account travel the exact same path.
        var user = TestHelpers.CreateUser();
        ArrangeOutstandingCode(user, matches: true);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, ValidOtp), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.DeleteAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _otpHasherMock.Verify(
            h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), user.PasswordHash!), Times.Never,
            "the stored password hash must never be consulted by the deletion flow");
    }

    [Fact]
    public async Task Handle_WrongOtp_RefusesGenericallyWithoutSideEffects()
    {
        var user = TestHelpers.CreateUser();
        var verification = ArrangeOutstandingCode(user, matches: false);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, "000000"), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.InvalidOtp);
        _verificationRepositoryMock.Verify(
            r => r.IncrementAttemptCountAsync(verification.Id, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoOutstandingCode_RefusesWithoutSideEffects()
    {
        // A session alone is not re-authentication: without a code that was
        // delivered to the mailbox there is nothing to verify.
        var user = TestHelpers.CreateUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountDeletionVerification>());

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, ValidOtp), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.InvalidOtp);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CodeBoundToAnotherAccount_RefusesWithoutSideEffects()
    {
        // A row left behind by a hard-deleted account whose address was later
        // re-registered must never confirm the new owner's deletion.
        var user = TestHelpers.CreateUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var strayVerification = AccountDeletionVerification.Create(Guid.NewGuid(), user.Email, OtpHash);
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { strayVerification });
        _otpHasherMock.Setup(h => h.Verify(It.IsAny<string>(), ValidOtp, OtpHash)).Returns(true);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, ValidOtp), CancellationToken.None);

        result.FirstError.Should().Be(AccountDeletionErrors.InvalidOtp);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewerCodeIssuedForTheSameAddress_DoesNotOrphanTheCodeTheUserIsHolding()
    {
        // Anyone who knows an address can mint a newer code for it through the
        // anonymous public endpoint. If only the newest row were redeemable,
        // that would remotely deny a signed-in user their own deletion.
        var user = TestHelpers.CreateUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var held = AccountDeletionVerification.Create(user.Id, user.Email, OtpHash);
        var newer = AccountDeletionVerification.Create(user.Id, user.Email, "someone-elses-hash");
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { newer, held });
        _otpHasherMock.Setup(h => h.Verify(It.IsAny<string>(), ValidOtp, OtpHash)).Returns(true);
        _otpHasherMock.Setup(h => h.Verify(It.IsAny<string>(), ValidOtp, "someone-elses-hash")).Returns(false);

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, ValidOtp), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _verificationRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(held.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OwnedOrganizationBlocksDeletion_RefusesBeforeSpendingTheCode()
    {
        // The conflict is deterministic and knowable up front, so discovering
        // it must not cost the user their single-use code.
        var user = TestHelpers.CreateUser();
        ArrangeOutstandingCode(user, matches: true);
        var organization = TestHelpers.CreateOrganization(ownerId: user.Id);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { organization });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [organization.Id] = 3 });

        var result = await _handler.Handle(
            new RequestAccountDeletionCommand(user.Id, ValidOtp), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _verificationRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Arranges the user lookup plus an outstanding code that does (or does not) match.</summary>
    private AccountDeletionVerification ArrangeOutstandingCode(User user, bool matches)
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var verification = AccountDeletionVerification.Create(user.Id, user.Email, OtpHash);
        _verificationRepositoryMock
            .Setup(r => r.GetValidForEmailAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { verification });
        _otpHasherMock
            .Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), OtpHash))
            .Returns(matches);

        return verification;
    }
}
