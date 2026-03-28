using Auth.Application.Features.Authentication.GetUserSessions;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// Unit tests for GetUserSessionsQueryHandler.
/// </summary>
public class GetActiveSessionsQueryHandlerTests
{
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock;
    private readonly Mock<ILogger<GetUserSessionsQueryHandler>> _loggerMock;
    private readonly GetUserSessionsQueryHandler _handler;

    public GetActiveSessionsQueryHandlerTests()
    {
        _sessionRepositoryMock = new Mock<IUserSessionRepository>();
        _loggerMock = new Mock<ILogger<GetUserSessionsQueryHandler>>();

        _handler = new GetUserSessionsQueryHandler(
            _sessionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NoActiveSessions_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = new GetUserSessionsQuery(userId);
        var sessions = new List<Auth.Domain.Entities.UserSession>();

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithActiveSessions_ReturnsMappedSessionDtos()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var query = new GetUserSessionsQuery(userId);

        var session1 = TestHelpers.CreateUserSession(
            userId: userId,
            applicationId: applicationId,
            ipAddress: "192.168.1.1",
            userAgent: "Chrome/120",
            deviceName: "Desktop",
            location: "New York",
            isActive: true);

        var session2 = TestHelpers.CreateUserSession(
            userId: userId,
            applicationId: applicationId,
            ipAddress: "10.0.0.1",
            userAgent: "Firefox/119",
            isActive: true);

        var sessions = new List<Auth.Domain.Entities.UserSession> { session1, session2 };

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);

        var dto1 = result.Value.First(d => d.Id == session1.Id);
        dto1.UserId.Should().Be(userId);
        dto1.ApplicationId.Should().Be(applicationId);
        dto1.IpAddress.Should().Be("192.168.1.1");
        dto1.UserAgent.Should().Be("Chrome/120");
        dto1.DeviceName.Should().Be("Desktop");
        dto1.Location.Should().Be("New York");
        dto1.IsActive.Should().BeTrue();
        dto1.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithCurrentSessionId_MarksCurrentSession()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var query = new GetUserSessionsQuery(userId, currentSessionId);

        var currentSession = TestHelpers.CreateUserSession(
            id: currentSessionId,
            userId: userId,
            isActive: true);

        var otherSession = TestHelpers.CreateUserSession(
            id: otherSessionId,
            userId: userId,
            isActive: true);

        var sessions = new List<Auth.Domain.Entities.UserSession> { currentSession, otherSession };

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);

        var currentDto = result.Value.First(d => d.Id == currentSessionId);
        currentDto.IsCurrent.Should().BeTrue();

        var otherDto = result.Value.First(d => d.Id == otherSessionId);
        otherDto.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithoutCurrentSessionId_AllSessionsNotCurrent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = new GetUserSessionsQuery(userId);

        var session = TestHelpers.CreateUserSession(userId: userId, isActive: true);
        var sessions = new List<Auth.Domain.Entities.UserSession> { session };

        _sessionRepositoryMock
            .Setup(r => r.GetActiveSessionsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(1);
        result.Value.First().IsCurrent.Should().BeFalse();
    }
}
