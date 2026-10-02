using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for EnableTwoFactorCommandHandler.
/// </summary>
public class EnableTwoFactorCommandHandlerTests
{
    private const long MatchedStep = 59_313_872;

    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorRepositoryMock;
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<EnableTwoFactorCommandHandler>> _loggerMock;
    private readonly EnableTwoFactorCommandHandler _handler;

    public EnableTwoFactorCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _twoFactorRepositoryMock = new Mock<ITwoFactorAuthRepository>();
        _stateStoreMock = new Mock<ITwoFactorStateStore>();
        _totpServiceMock = new Mock<ITotpService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<EnableTwoFactorCommandHandler>>();

        _handler = new EnableTwoFactorCommandHandler(
            _userRepositoryMock.Object,
            _twoFactorRepositoryMock.Object,
            _stateStoreMock.Object,
            _totpServiceMock.Object,
            new TotpReplayPolicy(TestHelpers.CreateOptions(new TwoFactorSettings())),
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NoTwoFactorSetup_ReturnsSetupRequiredError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.TwoFactorAuth?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.SetupRequired");
    }

    [Fact]
    public async Task Handle_TwoFactorAlreadyEnabled_ReturnsAlreadyEnabledError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: true);

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.TwoFactorAlreadyEnabled");
    }

    [Fact]
    public async Task Handle_InvalidTotpCode_ReturnsInvalidCodeError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "000000");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false, secretKey: "TESTSECRET");

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
    }

    [Fact]
    public async Task Handle_ValidCode_EnablesTwoFactorAndReturnsRecoveryCodes()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false, secretKey: "TESTSECRET");
        var user = TestHelpers.CreateUser(id: userId);
        var recoveryCodes = new[] { "CODE1", "CODE2", "CODE3" };

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(MatchedStep);

        _totpServiceMock
            .Setup(s => s.GenerateRecoveryCodes(10))
            .Returns(recoveryCodes);

        _totpServiceMock
            .Setup(s => s.HashRecoveryCode(It.IsAny<string>()))
            .Returns<string>(c => $"hashed_{c}");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().BeEquivalentTo(recoveryCodes);

        _twoFactorRepositoryMock.Verify(
            r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(
            r => r.UpdateAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCodeButUserNotFound_EnablesTwoFactorWithoutUserUpdate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new EnableTwoFactorCommand(userId, "123456");
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false, secretKey: "TESTSECRET");
        var recoveryCodes = new[] { "CODE1", "CODE2" };

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(MatchedStep);

        _totpServiceMock
            .Setup(s => s.GenerateRecoveryCodes(10))
            .Returns(recoveryCodes);

        _totpServiceMock
            .Setup(s => s.HashRecoveryCode(It.IsAny<string>()))
            .Returns<string>(c => $"hashed_{c}");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().BeEquivalentTo(recoveryCodes);

        _userRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── The step claim ─────────────────────────────────────────────────────

    private Auth.Domain.Entities.User GivenPendingFactorAndValidCode(Guid userId)
    {
        var twoFactor = TestHelpers.CreateTwoFactorAuth(userId: userId, isEnabled: false, secretKey: "TESTSECRET");
        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);
        var user = TestHelpers.CreateUser(id: userId);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _totpServiceMock
            .Setup(s => s.ValidateCode("TESTSECRET", "123456"))
            .Returns(MatchedStep);
        _totpServiceMock
            .Setup(s => s.GenerateRecoveryCodes(10))
            .Returns(["CODE1", "CODE2"]);
        _totpServiceMock
            .Setup(s => s.HashRecoveryCode(It.IsAny<string>()))
            .Returns<string>(c => $"hashed_{c}");
        return user;
    }

    [Fact]
    public async Task Enable_ClaimsTheMatchedStep()
    {
        // The code that switches the factor on is claimed once the factor is on —
        // the row AND the account flag sign-in reads — and with the step that code
        // matched, so it cannot go on to sign in or switch the factor off again.
        var userId = Guid.NewGuid();
        GivenPendingFactorAndValidCode(userId);
        var order = new List<string>();
        _twoFactorRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Auth.Domain.Entities.TwoFactorAuth>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("enable"))
            .Returns(Task.CompletedTask);
        _userRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Auth.Domain.Entities.User>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("account flag"))
            .Returns(Task.CompletedTask);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("claim"))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await _handler.Handle(new EnableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _stateStoreMock.Verify(
            s => s.TryClaimTotpStepAsync(userId, MatchedStep, true, It.IsAny<CancellationToken>()),
            Times.Once);
        order.Should().Equal("enable", "account flag", "claim");
    }

    [Theory]
    [InlineData(LoginCommitOutcome.StepReused)]
    [InlineData(LoginCommitOutcome.FactorLost)]
    public async Task Enable_IgnoresTheClaimsAnswer(LoginCommitOutcome claim)
    {
        // The code was proved a moment ago and the factor is already on; the claim
        // only protects what comes after. Whatever it answers, the user gets the
        // recovery codes the factor now holds.
        var userId = Guid.NewGuid();
        GivenPendingFactorAndValidCode(userId);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var result = await _handler.Handle(new EnableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RecoveryCodes.Should().BeEquivalentTo(["CODE1", "CODE2"]);
    }

    [Fact]
    public async Task Enable_StepClaimFaults_Propagates_WithTwoFactorFullyOn()
    {
        // The answer is ignored, the fault is not: a claim the database could not
        // run reaches the central handler instead of passing for a success. And it
        // runs last, so the fault finds two-factor fully on — the row and the
        // account flag sign-in reads — never a row that is on while the account
        // says off, which sign-in would not enforce and the profile could not
        // switch off. The way out is the profile's own: disable, enable again.
        var userId = Guid.NewGuid();
        var user = GivenPendingFactorAndValidCode(userId);
        _stateStoreMock
            .Setup(s => s.TryClaimTotpStepAsync(userId, MatchedStep, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("claim faulted"));

        var act = () => _handler.Handle(new EnableTwoFactorCommand(userId, "123456"), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        user.TwoFactorEnabled.Should().BeTrue();
        _userRepositoryMock.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }
}
