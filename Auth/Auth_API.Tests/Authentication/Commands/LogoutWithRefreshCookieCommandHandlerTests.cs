using Auth.Application.Features.Authentication.LogoutWithRefreshCookie;
using Auth.Application.Interfaces;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The cookie sign-out ends the cookie's session through the same revocation the
/// bearer sign-out uses, and never a session the browser did not mean.
/// </summary>
public class LogoutWithRefreshCookieCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _tokens = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly LogoutWithRefreshCookieCommandHandler _handler;

    public LogoutWithRefreshCookieCommandHandlerTests()
    {
        _keys.Setup(k => k.ComputeTokenHash("cookie-token")).Returns("cookie-hash");
        _handler = new LogoutWithRefreshCookieCommandHandler(
            _tokens.Object, _keys.Object, _revocation.Object, _publisher.Object,
            new Mock<ILogger<LogoutWithRefreshCookieCommandHandler>>().Object);
    }

    private RefreshTokenEntity Stored(Guid? sessionId, DateTime? revokedAt = null)
    {
        var token = TestHelpers.CreateRefreshToken(
            sessionId: sessionId, expiresAt: DateTime.UtcNow.AddDays(7), revokedAt: revokedAt);
        _tokens.Setup(r => r.GetByTokenHashAsync("cookie-hash", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        return token;
    }

    [Fact]
    public async Task TheCookiesSession_IsTerminated_WithItsSsoSession()
    {
        var session = Guid.NewGuid();
        var token = Stored(session);

        var result = await _handler.Handle(new LogoutWithRefreshCookieCommand("cookie-token", session, "sso"), default);

        result.Value.Ended.Should().BeTrue();
        _revocation.Verify(r => r.TerminateSessionAsync(session, token.UserId, "logout", It.IsAny<CancellationToken>()), Times.Once());
        _revocation.Verify(r => r.RevokeIdpSessionAsync("sso", It.IsAny<CancellationToken>()), Times.Once());
        _publisher.Verify(p => p.Publish(It.Is<UserLoggedOutEvent>(e => e.UserId == token.UserId), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task WithoutAnExpectedSession_TheCookiesSessionIsTerminated()
    {
        var session = Guid.NewGuid();
        Stored(session);

        (await _handler.Handle(new LogoutWithRefreshCookieCommand("cookie-token", null, null), default)).Value.Ended
            .Should().BeTrue();

        _revocation.Verify(r => r.TerminateSessionAsync(session, It.IsAny<Guid?>(), "logout", It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task ACookieFromANewerSignIn_IsNotEnded()
    {
        Stored(Guid.NewGuid());

        var result = await _handler.Handle(new LogoutWithRefreshCookieCommand("cookie-token", Guid.NewGuid(), "sso"), default);

        result.Value.Ended.Should().BeFalse();
        _revocation.VerifyNoOtherCalls();
        _publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownCookie_IsAlreadyEnded()
    {
        var result = await _handler.Handle(new LogoutWithRefreshCookieCommand("cookie-token", Guid.NewGuid(), null), default);

        result.Value.Ended.Should().BeTrue();
        _revocation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ASessionlessToken_IsRevokedItself()
    {
        var token = Stored(sessionId: null);

        (await _handler.Handle(new LogoutWithRefreshCookieCommand("cookie-token", null, null), default)).Value.Ended
            .Should().BeTrue();

        _tokens.Verify(r => r.UpdateAsync(It.Is<RefreshTokenEntity>(t => t.Id == token.Id && t.IsRevoked), It.IsAny<CancellationToken>()), Times.Once());
    }
}
