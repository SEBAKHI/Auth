using Auth.Application.Features.Authentication.TerminateAllSessions;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for TerminateAllSessionsCommandHandler.
/// </summary>
public class TerminateAllSessionsCommandHandlerTests
{
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock;
    private readonly Mock<ILogger<TerminateAllSessionsCommandHandler>> _loggerMock;
    private readonly TerminateAllSessionsCommandHandler _handler;

    public TerminateAllSessionsCommandHandlerTests()
    {
        _sessionRepositoryMock = new Mock<IUserSessionRepository>();
        _loggerMock = new Mock<ILogger<TerminateAllSessionsCommandHandler>>();

        _handler = new TerminateAllSessionsCommandHandler(
            _sessionRepositoryMock.Object,
            new Mock<IRefreshTokenRepository>().Object,
            new Mock<ITokenBlacklistService>().Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NoExceptSessionId_TerminatesAllSessionsAndReturnsCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new TerminateAllSessionsCommand(userId);
        var sessions = new List<Auth.Domain.Entities.UserSession>
        {
            TestHelpers.CreateUserSession(userId: userId, isActive: true),
            TestHelpers.CreateUserSession(userId: userId, isActive: true),
            TestHelpers.CreateUserSession(userId: userId, isActive: true)
        };

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(3);

        _sessionRepositoryMock.Verify(
            r => r.TerminateAllForUserAsync(userId, "User terminated all sessions", It.IsAny<CancellationToken>()),
            Times.Once);
        _sessionRepositoryMock.Verify(
            r => r.TerminateOtherSessionsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithExceptSessionId_TerminatesOtherSessionsAndReturnsCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var command = new TerminateAllSessionsCommand(userId, currentSessionId);
        var sessions = new List<Auth.Domain.Entities.UserSession>
        {
            TestHelpers.CreateUserSession(id: currentSessionId, userId: userId, isActive: true),
            TestHelpers.CreateUserSession(userId: userId, isActive: true),
            TestHelpers.CreateUserSession(userId: userId, isActive: true)
        };

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(2);

        _sessionRepositoryMock.Verify(
            r => r.TerminateOtherSessionsAsync(
                userId,
                currentSessionId,
                "User terminated all other sessions",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _sessionRepositoryMock.Verify(
            r => r.TerminateAllForUserAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoActiveSessions_ReturnsZero()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new TerminateAllSessionsCommand(userId);
        var sessions = new List<Auth.Domain.Entities.UserSession>();

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithExceptSessionIdAndOnlyCurrentSession_ReturnsZero()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var command = new TerminateAllSessionsCommand(userId, currentSessionId);
        var sessions = new List<Auth.Domain.Entities.UserSession>
        {
            TestHelpers.CreateUserSession(id: currentSessionId, userId: userId, isActive: true)
        };

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(0);
    }
}
