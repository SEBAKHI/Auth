using Auth.Application.Features.Authentication.TerminateAllSessions;
using Auth.Application.Interfaces;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for TerminateAllSessionsCommandHandler. The session mechanics
/// live in CredentialRevocationService (tested separately); the handler's job
/// is correct delegation and reason selection.
/// </summary>
public class TerminateAllSessionsCommandHandlerTests
{
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly TerminateAllSessionsCommandHandler _handler;

    public TerminateAllSessionsCommandHandlerTests()
    {
        _handler = new TerminateAllSessionsCommandHandler(_credentialRevocationMock.Object);
    }

    [Fact]
    public async Task Handle_NoExceptSessionId_RevokesEverythingAndReturnsCount()
    {
        var userId = Guid.NewGuid();
        var command = new TerminateAllSessionsCommand(userId);
        _credentialRevocationMock
            .Setup(s => s.RevokeCredentialsAsync(userId, null, null, userId, "User terminated all sessions", It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be(3);
        _credentialRevocationMock.Verify(
            s => s.RevokeCredentialsAsync(userId, null, null, userId, "User terminated all sessions", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithExceptSessionId_SparesThisBrowserAndReturnsCount()
    {
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var command = new TerminateAllSessionsCommand(userId, currentSessionId, "sso-cookie");
        _credentialRevocationMock
            .Setup(s => s.RevokeCredentialsAsync(userId, currentSessionId, "sso-cookie", userId, "User terminated all other sessions", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be(2);
        _credentialRevocationMock.Verify(
            s => s.RevokeCredentialsAsync(userId, currentSessionId, "sso-cookie", userId, "User terminated all other sessions", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NeverMerelyTerminatesSessions()
    {
        // The regression this guards: delegating to TerminateSessionsAsync ended
        // the UserSessions rows and left the SSO sessions minting authorization
        // codes, so "sign out everywhere" reported a count and locked nothing out.
        var userId = Guid.NewGuid();

        await _handler.Handle(new TerminateAllSessionsCommand(userId, Guid.NewGuid()), CancellationToken.None);

        _credentialRevocationMock.Verify(
            s => s.TerminateSessionsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoActiveSessions_ReturnsZero()
    {
        var userId = Guid.NewGuid();
        var command = new TerminateAllSessionsCommand(userId);
        _credentialRevocationMock
            .Setup(s => s.RevokeCredentialsAsync(userId, null, null, userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be(0);
    }
}
