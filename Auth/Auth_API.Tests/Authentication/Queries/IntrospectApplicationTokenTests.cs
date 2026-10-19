using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// The contract the single-logout docs state (OI-155, REVIEW 84 F1): introspection is a platform
/// endpoint that validates the platform audience only, so an application's access token, which
/// carries the application's code as its audience, introspects inactive whether its session is
/// live or not. An application learns of a sign-out at its next refresh, not by introspecting.
/// A change that opens introspection to applications fails here, and must revisit those docs.
/// </summary>
public sealed class IntrospectApplicationTokenTests : IDisposable
{
    private const string ApplicationCode = "CRM";

    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly IntrospectTokenQueryHandler _handler;
    private readonly Auth.Domain.Entities.User _user = TestHelpers.CreateUser();

    public IntrospectApplicationTokenTests()
    {
        // The real token service, so the audience rule is the one the API applies.
        _handler = new IntrospectTokenQueryHandler(
            _tokens.Service, _blacklist.Object, new Mock<IRefreshTokenRepository>().Object,
            new Mock<IRefreshTokenKeyService>().Object, NullLogger<IntrospectTokenQueryHandler>.Instance);
    }

    public void Dispose() => _tokens.Dispose();

    private Task<IntrospectTokenResponse> Introspect(string token) =>
        _handler.Handle(new IntrospectTokenQuery(token, TokenTypeHint.AccessToken), CancellationToken.None)
            .ContinueWith(t => t.Result.Value);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_AnApplicationsAccessToken_IsInactive_WhetherItsSessionIsLiveOrNot(bool sessionEnded)
    {
        var sessionId = Guid.NewGuid();
        _blacklist.Setup(b => b.IsSessionBlacklisted(sessionId.ToString())).Returns(sessionEnded);

        var response = await Introspect(_tokens.ForApplication(_user, "openid", ApplicationCode, sessionId));

        response.Active.Should().BeFalse("introspection validates the platform audience only");
    }

    [Fact]
    public async Task Handle_APlatformAccessToken_IsActiveWhileItsSessionLives_AndInactiveOnceItEnds()
    {
        // The control: the same handler and key do answer active, so the inactive answer above
        // is the audience rule, not a token this test cannot validate.
        var token = _tokens.ForPlatform(_user);

        (await Introspect(token)).Active.Should().BeTrue();

        _blacklist.Setup(b => b.IsSessionBlacklisted(It.IsAny<string>())).Returns(true);
        (await Introspect(token)).Active.Should().BeFalse("a platform caller hears of the ended session at once (A5)");
    }
}
