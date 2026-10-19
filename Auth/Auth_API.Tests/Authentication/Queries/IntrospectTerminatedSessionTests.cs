using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// OI-155 A5: introspection reads the session blacklist the platform's own API reads, so an access
/// token whose session was terminated (a sign-out, a session ended from the list, a single logout)
/// answers inactive at once instead of until it expires. Independent of the single-logout switch.
/// </summary>
public class IntrospectTerminatedSessionTests
{
    private const string Token = "header.payload.signature";

    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly IntrospectTokenQueryHandler _handler;
    private readonly string _sessionId = Guid.NewGuid().ToString();

    public IntrospectTerminatedSessionTests()
    {
        _jwt.Setup(s => s.ValidateAccessToken(Token)).Returns(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("jti", "token-jti"),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("sid", _sessionId),
            new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        ])));

        _handler = new IntrospectTokenQueryHandler(
            _jwt.Object, _blacklist.Object, new Mock<IRefreshTokenRepository>().Object,
            new Mock<IRefreshTokenKeyService>().Object, NullLogger<IntrospectTokenQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_AnAccessTokenWhoseSessionWasTerminated_ReturnsInactive()
    {
        _blacklist.Setup(s => s.IsSessionBlacklisted(_sessionId)).Returns(true);

        var result = await _handler.Handle(new IntrospectTokenQuery(Token, TokenTypeHint.AccessToken), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeFalse("the platform's own API refuses this token from the session's end, and so must introspection");
        result.Value.Sub.Should().BeNull("an inactive answer carries nothing else (RFC 7662 §2.2)");
    }

    [Fact]
    public async Task Handle_AnAccessTokenWhoseSessionIsLive_ReturnsActive()
    {
        var result = await _handler.Handle(new IntrospectTokenQuery(Token, TokenTypeHint.AccessToken), CancellationToken.None);

        result.Value.Active.Should().BeTrue();
        _blacklist.Verify(s => s.IsSessionBlacklisted(_sessionId), Times.Once, "the session is what was asked about");
    }
}
