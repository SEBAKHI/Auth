using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for DisableTwoFactorCommandHandler.
/// </summary>
public class DisableTwoFactorCommandHandlerTests
{
    private const long MatchedStep = 59_313_872;

    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorRepositoryMock;
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<DisableTwoFactorCommandHandler>> _loggerMock;
    private readonly TwoFactorSettings _twoFactorSettings = new();
    private readonly DisableTwoFactorCommandHandler _handler;

    public DisableTwoFactorCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _twoFactorRepositoryMock = new Mock<ITwoFactorAuthRepository>();
        _stateStoreMock = new Mock<ITwoFactorStateStore>();
        _totpServiceMock = new Mock<ITotpService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<DisableTwoFactorCommandHandler>>();

        _handler = new DisableTwoFactorCommandHandler(
            _userRepositoryMock.Object,
            _twoFactorRepositoryMock.Object,
            _stateStoreMock.Object,
            _totpServiceMock.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(_twoFactorSettings)),
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// An enabled factor whose code checks out, and the step claim's answer. The
    /// claim answers whether THIS request may count the code, so a loose mock —
    /// answering zero — would read as a refusal; every path states its answer.
    /// </summary>
    private Auth.Domain.Entities.TwoFactorAuth GivenValidCode(Guid userId, LoginCommitOutcome claim)
    {
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: true, secretKey: "TESTSECRET");
        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);
        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(MatchedStep);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);
        return twoFactor;
    }

    [Fact]
    public async Task Handle_NoTwoFactorConfig_ReturnsTwoFactorNotEnabledError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.TwoFactorAuth?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.TwoFactorNotEnabled");
    }

    [Fact]
    public async Task Handle_TwoFactorNotEnabled_ReturnsTwoFactorNotEnabledError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.TwoFactorNotEnabled");
    }

    [Fact]
    public async Task Handle_TwoFactorLockedOut_ReturnsLockedOutError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            lockedUntil: DateTime.UtcNow.AddMinutes(10));

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.LockedOut");
    }

    [Fact]
    public async Task Handle_InvalidTotpCode_RecordsFailureAndReturnsInvalidCodeError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "000000");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            secretKey: "TESTSECRET");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "000000"))
            .Returns((long?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidTwoFactorCode");

        _twoFactorRepositoryMock.Verify(
            r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCode_DisablesTwoFactorAndDispatchesEvents()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            secretKey: "TESTSECRET");
        var user = TestHelpers.CreateUser(id: userId);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(MatchedStep);

        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCodeButUserNotFound_DisablesTwoFactorWithoutUserUpdate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new DisableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            secretKey: "TESTSECRET");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(MatchedStep);

        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── The step claim: a code counts once ─────────────────────────────────

    [Fact]
    public async Task ReusedStep_ReturnsCodeAlreadyUsed_AndKeepsTwoFactor()
    {
        // A code the account already accepted — typed by its owner to sign in,
        // then presented from a stolen session to switch the factor off. The
        // claim runs before anything is removed, so the factor stays.
        var userId = Guid.Parse("0f0e0d0c-0b0a-4908-8706-050403020100");
        var twoFactor = GivenValidCode(userId, LoginCommitOutcome.StepReused);

        var result = await _handler.Handle(new DisableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.CodeAlreadyUsed");
        _stateStoreMock.Verify(
            s => s.TryClaimTotpStepAsync(userId, MatchedStep, true, It.IsAny<CancellationToken>()),
            Times.Once);
        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Refused like a wrong code, and counted like one.
        twoFactor.FailedAttempts.Should().Be(1);
        _twoFactorRepositoryMock.Verify(r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()), Times.Once);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("Reused two-factor code rejected")
                    && v.ToString()!.Contains("disable")
                    && !v.ToString()!.Contains("123456")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCode_ClaimsTheStepBeforeRemovingTheFactor()
    {
        var userId = Guid.NewGuid();
        GivenValidCode(userId, LoginCommitOutcome.Committed);
        var order = new List<string>();
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, true, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("claim"))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        _twoFactorRepositoryMock
            .Setup(r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("delete"))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new DisableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        order.Should().Equal("claim", "delete");
    }

    [Fact]
    public async Task Handle_FactorGoneUnderTheClaim_ReturnsTwoFactorNotEnabled()
    {
        // Switched off or removed between the read and the claim: the same answer
        // the read itself gives for a factor that is not on, and nothing removed.
        var userId = Guid.NewGuid();
        GivenValidCode(userId, LoginCommitOutcome.FactorLost);

        var result = await _handler.Handle(new DisableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.TwoFactorNotEnabled");
        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_StepClaimFaults_PropagatesAndKeepsTwoFactor()
    {
        // Fail-closed: a claim the database could not run (a missing column, a
        // timeout) is neither treated as accepted nor skipped. The fault reaches
        // the central handler and the factor stays on.
        var userId = Guid.NewGuid();
        GivenValidCode(userId, LoginCommitOutcome.Committed);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("claim faulted"));

        var act = () => _handler.Handle(new DisableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        _twoFactorRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReuseAcceptedWithTheSwitchOff_DisablesAndLogsTheReuse()
    {
        _twoFactorSettings.RejectReusedCodes = false;
        var userId = Guid.NewGuid();
        GivenValidCode(userId, LoginCommitOutcome.Committed);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.ReuseAccepted);

        var result = await _handler.Handle(new DisableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _twoFactorRepositoryMock.Verify(r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Reused two-factor code accepted (RejectReusedCodes=false)")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
