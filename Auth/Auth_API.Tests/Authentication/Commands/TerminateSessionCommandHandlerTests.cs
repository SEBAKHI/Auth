using Auth.Application.Features.Authentication.TerminateSession;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for TerminateSessionCommandHandler.
/// </summary>
public class TerminateSessionCommandHandlerTests
{
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock;
    private readonly Mock<ILogger<TerminateSessionCommandHandler>> _loggerMock;
    private readonly TerminateSessionCommandHandler _handler;

    public TerminateSessionCommandHandlerTests()
    {
        _sessionRepositoryMock = new Mock<IUserSessionRepository>();
        _loggerMock = new Mock<ILogger<TerminateSessionCommandHandler>>();

        _handler = new TerminateSessionCommandHandler(
            _sessionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_SessionNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = new TerminateSessionCommand(userId, sessionId);

        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.UserSession?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Session.NotFound");
    }

    [Fact]
    public async Task Handle_SessionBelongsToDifferentUser_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = new TerminateSessionCommand(userId, sessionId);
        var session = TestHelpers.CreateUserSession(id: sessionId, userId: otherUserId, isActive: true);

        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Session.NotFound");
    }

    [Fact]
    public async Task Handle_SessionAlreadyTerminated_ReturnsAlreadyTerminatedError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = new TerminateSessionCommand(userId, sessionId);
        var session = TestHelpers.CreateUserSession(
            id: sessionId,
            userId: userId,
            isActive: false,
            terminatedAt: DateTime.UtcNow,
            terminationReason: "Previously terminated");

        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Session.AlreadyTerminated");
    }

    [Fact]
    public async Task Handle_ValidActiveSession_TerminatesSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = new TerminateSessionCommand(userId, sessionId);
        var session = TestHelpers.CreateUserSession(id: sessionId, userId: userId, isActive: true);

        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _sessionRepositoryMock.Verify(
            r => r.TerminateAsync(sessionId, "User terminated", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
