using Auth.Application.Features.Authentication.TerminateSession;
using Auth.Application.Interfaces;
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
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock;
    private readonly Mock<ILogger<TerminateSessionCommandHandler>> _loggerMock;
    private readonly TerminateSessionCommandHandler _handler;

    public TerminateSessionCommandHandlerTests()
    {
        _sessionRepositoryMock = new Mock<IUserSessionRepository>();
        _credentialRevocationMock = new Mock<ICredentialRevocationService>();
        _loggerMock = new Mock<ILogger<TerminateSessionCommandHandler>>();

        _handler = new TerminateSessionCommandHandler(
            _sessionRepositoryMock.Object,
            _credentialRevocationMock.Object,
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

        // X04: the one credential-kill primitive ends it (refresh tokens, row, session id
        // blacklisted until the revocation horizon), never moves of the handler's own.
        _credentialRevocationMock.Verify(
            s => s.TerminateSessionAsync(sessionId, userId, "User terminated", It.IsAny<CancellationToken>()),
            Times.Once);
        _sessionRepositoryMock.Verify(
            r => r.TerminateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RefusedSession_KillsNothing()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(id: sessionId, userId: Guid.NewGuid(), isActive: true));

        var result = await _handler.Handle(new TerminateSessionCommand(userId, sessionId), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _credentialRevocationMock.Verify(
            s => s.TerminateSessionAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
