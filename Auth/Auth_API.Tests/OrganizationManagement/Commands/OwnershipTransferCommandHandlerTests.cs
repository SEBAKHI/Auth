using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.InitiateOwnershipTransfer;
using Auth.Application.Features.Organizations.TransferOwnership;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.OrganizationManagement.Commands;

/// <summary>
/// Tests for the two ownership transfer handlers: initiation (code emailed to
/// the prospective new owner) and completion (code verification + atomic swap).
/// </summary>
public class OwnershipTransferCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IOwnershipTransferCodeRepository> _transferCodeRepositoryMock = new();
    private readonly Mock<IOtpGenerator> _otpGeneratorMock = new();
    private readonly Mock<IOtpHasher> _otpHasherMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();

    // Left without a Setup on purpose: Moq defaults the bool to false, which is
    // the production shape, so every existing Initiate test runs with the
    // transfer-code log shut.
    private readonly Mock<IEnvironmentInfo> _environmentInfoMock = new();

    // Hoisted out of CreateInitiateHandler: the factory used to build a logger
    // mock inline and drop it, so nothing could assert on what this handler logs.
    private readonly Mock<ILogger<InitiateOwnershipTransferCommandHandler>> _initiateLoggerMock = new();

    private static Organization CreateOrganization(Guid ownerId, bool isAutoCreated = false) => new(
        id: Guid.NewGuid(),
        code: $"org-{Guid.NewGuid():N}"[..20],
        name: "Test Org",
        description: null,
        logoUrl: null,
        website: null,
        contactEmail: "org@test.com",
        ownerId: ownerId,
        isActive: true,
        isAutoCreated: isAutoCreated,
        createdAt: DateTime.UtcNow,
        createdBy: ownerId,
        modifiedAt: null,
        modifiedBy: null);

    private InitiateOwnershipTransferCommandHandler CreateInitiateHandler() => new(
        _organizationRepositoryMock.Object,
        _userRepositoryMock.Object,
        _transferCodeRepositoryMock.Object,
        _otpGeneratorMock.Object,
        _otpHasherMock.Object,
        _notificationServiceMock.Object,
        _publisherMock.Object,
        TestHelpers.CreateOptions(new EmailSettings { Enabled = false }),
        _environmentInfoMock.Object,
        _initiateLoggerMock.Object);

    private TransferOwnershipCommandHandler CreateTransferHandler() => new(
        _organizationRepositoryMock.Object,
        _userRepositoryMock.Object,
        _roleRepositoryMock.Object,
        _transferCodeRepositoryMock.Object,
        _otpHasherMock.Object,
        _publisherMock.Object,
        new Mock<ILogger<TransferOwnershipCommandHandler>>().Object);

    /// <summary>
    /// Wires a fully valid transfer scenario and returns the pieces tests tweak.
    /// </summary>
    private (Organization Org, Guid OwnerId, Guid TargetId, Role OwnerRole, Role AdminRole) SetupValidScenario()
    {
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var org = CreateOrganization(ownerId);
        var target = TestHelpers.CreateUser(id: targetId);
        var owner = TestHelpers.CreateUser(id: ownerId);
        var ownerRole = TestHelpers.CreateRole(code: OrganizationRoleCodes.Owner);
        var adminRole = TestHelpers.CreateRole(code: OrganizationRoleCodes.Admin);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);
        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(org.Id, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: org.Id, userId: targetId));
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);
        _roleRepositoryMock
            .Setup(r => r.GetByCodeAsync((Guid?)null, OrganizationRoleCodes.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerRole);
        _roleRepositoryMock
            .Setup(r => r.GetByCodeAsync((Guid?)null, OrganizationRoleCodes.Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminRole);
        _organizationRepositoryMock
            .Setup(r => r.TransferOwnershipAsync(
                org.Id, ownerId, targetId, ownerRole.Id, adminRole.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _otpGeneratorMock.Setup(g => g.GenerateNumericOtp(6)).Returns("123456");
        _otpHasherMock.Setup(h => h.Hash(It.IsAny<string>(), "123456")).Returns("hashed-otp");
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        return (org, ownerId, targetId, ownerRole, adminRole);
    }

    #region Initiate

    [Fact]
    public async Task Initiate_NotOwner_ReturnsNotOwner()
    {
        var (org, _, targetId, _, _) = SetupValidScenario();
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = Guid.NewGuid() };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.NotOwner);
    }

    [Fact]
    public async Task Initiate_PersonalOrganization_ReturnsCannotTransferPersonalOrganization()
    {
        var ownerId = Guid.NewGuid();
        var org = CreateOrganization(ownerId, isAutoCreated: true);
        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);
        var command = new InitiateOwnershipTransferCommand(org.Id, Guid.NewGuid()) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.CannotTransferPersonalOrganization);
    }

    [Fact]
    public async Task Initiate_TargetIsOwner_ReturnsCannotTransferToSelf()
    {
        var (org, ownerId, _, _, _) = SetupValidScenario();
        var command = new InitiateOwnershipTransferCommand(org.Id, ownerId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.CannotTransferToSelf);
    }

    [Fact]
    public async Task Initiate_TargetNotMember_ReturnsCannotTransferOwnership()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(org.Id, targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser?)null);
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.CannotTransferOwnership);
    }

    [Fact]
    public async Task Initiate_TargetEmailNotConfirmed_ReturnsTransferTargetNotEligible()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: targetId, emailConfirmed: false));
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.TransferTargetNotEligible);
    }

    [Fact]
    public async Task Initiate_RateLimited_ReturnsTooManyTransferRequests()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        _transferCodeRepositoryMock
            .Setup(r => r.GetRecentCountForOrganizationAsync(org.Id, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(int.MaxValue);
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.TooManyTransferRequests);
    }

    [Fact]
    public async Task Initiate_Valid_StoresHashedCodeSendsEmailAndPublishesEvent()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.TargetEmailMasked.Should().Contain("*");
        _transferCodeRepositoryMock.Verify(
            r => r.InvalidateAllForOrganizationAsync(org.Id, It.IsAny<CancellationToken>()), Times.Once());
        _transferCodeRepositoryMock.Verify(
            r => r.CreateAsync(
                It.Is<OwnershipTransferCode>(c =>
                    c.OrganizationId == org.Id
                    && c.TargetUserId == targetId
                    && c.InitiatedBy == ownerId
                    && c.CodeHash == "hashed-otp"),
                It.IsAny<CancellationToken>()),
            Times.Once());
        _notificationServiceMock.Verify(
            s => s.SendAsync(
                It.Is<NotificationRequest>(n =>
                    n.TypeCode == NotificationTypeCodes.OwnershipTransferCode
                    && n.RecipientUserId == targetId
                    && Equals(n.Variables["OtpCode"], "123456")),
                It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<OrganizationOwnershipTransferInitiatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Initiate_EmailSendFails_ReturnsTransferCodeEmailFailed()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Notification.SendFailed", "boom"));
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.TransferCodeEmailFailed);
    }

    [Fact]
    public async Task Initiate_EmailDisabledOutsideDevelopment_DoesNotLogTheTransferCode()
    {
        // The six-digit code is one half of the transfer proof: whoever reads it can
        // hand it to the sitting owner and complete the ownership swap, and it is
        // written in plaintext beside the masked address. Email:Enabled is a hot
        // setting an operator can flip from the console in production, so the
        // environment is the only thing keeping the code out of a production log.
        // The mock defaults IsDevelopment to false.
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyTransferCodeLogged(Times.Never());
    }

    [Fact]
    public async Task Initiate_EmailDisabledInDevelopment_LogsTheTransferCode()
    {
        // The other half of the gate. With no mail server the log is the only place
        // the code exists, which is what makes the flow testable locally; a fix that
        // closed production by closing Development too would be a regression.
        _environmentInfoMock.Setup(e => e.IsDevelopment).Returns(true);
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        var command = new InitiateOwnershipTransferCommand(org.Id, targetId) { RequestedBy = ownerId };

        var result = await CreateInitiateHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyTransferCodeLogged(Times.Once());
    }

    /// <summary>
    /// Matches the message, not the level alone: this handler writes a second
    /// Warning - the per-organization rate limit - so the level on its own does not
    /// identify the leak line. The happy path these two tests drive never trips that
    /// limit, but a level-only assertion would silently start passing for the wrong
    /// reason if that ever changed.
    /// </summary>
    private void VerifyTransferCodeLogged(Times times) =>
        _initiateLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Ownership transfer code")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    #endregion

    #region Transfer

    private static OwnershipTransferCode CreateCode(Guid organizationId, Guid targetUserId, int attemptCount = 0) => new(
        id: Guid.NewGuid(),
        organizationId: organizationId,
        targetUserId: targetUserId,
        initiatedBy: Guid.NewGuid(),
        codeHash: "hashed-otp",
        expiresAt: DateTime.UtcNow.AddMinutes(10),
        usedAt: null,
        attemptCount: attemptCount,
        createdAt: DateTime.UtcNow);

    [Fact]
    public async Task Transfer_NotOwnerWithoutPlatformScope_ReturnsNotOwner()
    {
        var (org, _, targetId, _, _) = SetupValidScenario();
        var command = new TransferOwnershipCommand(org.Id, targetId, "123456") { RequestedBy = Guid.NewGuid() };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.NotOwner);
    }

    [Fact]
    public async Task Transfer_PersonalOrganization_BlockedEvenForPlatformScope()
    {
        var org = CreateOrganization(Guid.NewGuid(), isAutoCreated: true);
        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);
        var command = new TransferOwnershipCommand(org.Id, Guid.NewGuid(), null)
        {
            RequestedBy = Guid.NewGuid(),
            PlatformScope = true
        };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.CannotTransferPersonalOrganization);
    }

    [Fact]
    public async Task Transfer_OwnerWithoutCode_ReturnsTransferCodeRequired()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        var command = new TransferOwnershipCommand(org.Id, targetId, null) { RequestedBy = ownerId };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.TransferCodeRequired);
    }

    [Fact]
    public async Task Transfer_NoValidCode_ReturnsInvalidOrExpiredTransferCode()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        _transferCodeRepositoryMock
            .Setup(r => r.GetValidForOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnershipTransferCode?)null);
        var command = new TransferOwnershipCommand(org.Id, targetId, "123456") { RequestedBy = ownerId };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.InvalidOrExpiredTransferCode);
    }

    [Fact]
    public async Task Transfer_CodeBoundToDifferentTarget_ReturnsInvalidOrExpiredTransferCode()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        _transferCodeRepositoryMock
            .Setup(r => r.GetValidForOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCode(org.Id, targetUserId: Guid.NewGuid()));
        _otpHasherMock.Setup(h => h.Verify(It.IsAny<string>(), "123456", "hashed-otp")).Returns(true);
        var command = new TransferOwnershipCommand(org.Id, targetId, "123456") { RequestedBy = ownerId };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.InvalidOrExpiredTransferCode);
        _organizationRepositoryMock.Verify(
            r => r.TransferOwnershipAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Transfer_MaxAttemptsReached_ReturnsTransferCodeTooManyAttempts()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        _transferCodeRepositoryMock
            .Setup(r => r.GetValidForOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCode(org.Id, targetId, attemptCount: OwnershipTransferCode.MaxAttempts));
        var command = new TransferOwnershipCommand(org.Id, targetId, "123456") { RequestedBy = ownerId };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.TransferCodeTooManyAttempts);
    }

    [Fact]
    public async Task Transfer_WrongCode_IncrementsAttemptsAndReturnsInvalid()
    {
        var (org, ownerId, targetId, _, _) = SetupValidScenario();
        var code = CreateCode(org.Id, targetId);
        _transferCodeRepositoryMock
            .Setup(r => r.GetValidForOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);
        _otpHasherMock.Setup(h => h.Verify(It.IsAny<string>(), "999999", "hashed-otp")).Returns(false);
        var command = new TransferOwnershipCommand(org.Id, targetId, "999999") { RequestedBy = ownerId };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.InvalidOrExpiredTransferCode);
        _transferCodeRepositoryMock.Verify(
            r => r.IncrementAttemptCountAsync(code.Id, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Transfer_ValidCode_SwapsAtomicallyMarksUsedAndPublishesEvent()
    {
        var (org, ownerId, targetId, ownerRole, adminRole) = SetupValidScenario();
        var code = CreateCode(org.Id, targetId);
        _transferCodeRepositoryMock
            .Setup(r => r.GetValidForOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);
        _otpHasherMock.Setup(h => h.Verify(It.IsAny<string>(), "123456", "hashed-otp")).Returns(true);
        var command = new TransferOwnershipCommand(org.Id, targetId, "123456") { RequestedBy = ownerId };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _transferCodeRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(code.Id, It.IsAny<CancellationToken>()), Times.Once());
        _organizationRepositoryMock.Verify(
            r => r.TransferOwnershipAsync(
                org.Id, ownerId, targetId, ownerRole.Id, adminRole.Id, ownerId, It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<OrganizationOwnershipTransferredEvent>(e =>
                    e.OrganizationId == org.Id
                    && e.PreviousOwnerId == ownerId
                    && e.NewOwnerId == targetId
                    && !e.ViaPlatformScope),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Transfer_PlatformScopeNonOwner_TransfersWithoutCode()
    {
        var (org, ownerId, targetId, ownerRole, adminRole) = SetupValidScenario();
        var adminId = Guid.NewGuid();
        _organizationRepositoryMock
            .Setup(r => r.TransferOwnershipAsync(
                org.Id, ownerId, targetId, ownerRole.Id, adminRole.Id, adminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var command = new TransferOwnershipCommand(org.Id, targetId, null)
        {
            RequestedBy = adminId,
            PlatformScope = true
        };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _transferCodeRepositoryMock.Verify(
            r => r.GetValidForOrganizationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<OrganizationOwnershipTransferredEvent>(e => e.ViaPlatformScope),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Transfer_ConcurrentOwnerChange_ReturnsConcurrentTransferConflict()
    {
        var (org, ownerId, targetId, ownerRole, adminRole) = SetupValidScenario();
        var code = CreateCode(org.Id, targetId);
        _transferCodeRepositoryMock
            .Setup(r => r.GetValidForOrganizationAsync(org.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);
        _otpHasherMock.Setup(h => h.Verify(It.IsAny<string>(), "123456", "hashed-otp")).Returns(true);
        _organizationRepositoryMock
            .Setup(r => r.TransferOwnershipAsync(
                org.Id, ownerId, targetId, ownerRole.Id, adminRole.Id, ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var command = new TransferOwnershipCommand(org.Id, targetId, "123456") { RequestedBy = ownerId };

        var result = await CreateTransferHandler().Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(OrganizationErrors.ConcurrentTransferConflict);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<OrganizationOwnershipTransferredEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion
}
