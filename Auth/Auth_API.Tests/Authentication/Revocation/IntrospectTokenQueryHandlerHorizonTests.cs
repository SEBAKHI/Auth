using System.Threading.Channels;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Features.MachineCredentialChecks;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Common.Authentication;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// X04, RV2 (g): introspection reads the same blacklist the API does, so an application asking
/// about a token its user signed out of everywhere two hours ago hears "inactive", whatever the
/// token's remaining lifetime. Before X04 the entry was gone after one hour and the answer was
/// "active". The blacklist is the real one, on a clock this test moves; the token is a real one,
/// validated against the real clock.
/// </summary>
/// <remarks>Deliberate break: evict a user-level entry one hour after the revocation; red.</remarks>
public sealed class IntrospectTokenQueryHandlerHorizonTests : IDisposable
{
    private readonly UserInfoTokens _tokens = new();
    private readonly AdjustableTimeProvider _blacklistClock = new();
    private readonly TokenBlacklistService _blacklist;
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly IntrospectTokenQueryHandler _handler;
    private readonly User _user = TestHelpers.CreateUser();
    private readonly AppEntity _caller = TestHelpers.CreateApplication(code: UserInfoTokens.Application.ToUpperInvariant());

    public IntrospectTokenQueryHandlerHorizonTests()
    {
        _blacklist = new TokenBlacklistService(
            Channel.CreateUnbounded<TokenRevocation>().Writer, _blacklistClock, NullLogger<TokenBlacklistService>.Instance);
        _applications
            .Setup(r => r.GetByCodeAsync(_caller.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_caller);
        _handler = new IntrospectTokenQueryHandler(
            new ApplicationAccessTokenValidator(_tokens.UserInfoParameters),
            _applications.Object,
            _blacklist,
            new Mock<IRefreshTokenRepository>().Object,
            new FakeCredentialHasher(),
            NullLogger<IntrospectTokenQueryHandler>.Instance);
    }

    public void Dispose()
    {
        _blacklist.Dispose();
        _tokens.Dispose();
    }

    private async Task<bool> IsActive(string token) =>
        (await _handler.Handle(
            new IntrospectTokenQuery(
                new MachineCredentialCaller(_caller.Code, MachineClients.Scopes.TokensIntrospect), token, TokenTypeHint.AccessToken),
            CancellationToken.None)).Value.Active;

    [Fact]
    public async Task ATokenIssuedBeforeASignOutEverywhere_IsStillInactive_TwoHoursLater()
    {
        var token = _tokens.ForApplication(_user, "openid");
        (await IsActive(token)).Should().BeTrue();

        // iat is in whole seconds: the revocation a second later covers the token.
        _blacklist.BlacklistAllUserTokens(_user.Id, DateTime.UtcNow.AddSeconds(1));
        _blacklistClock.Advance(TimeSpan.FromHours(2));
        _blacklist.CleanupExpiredEntries();

        (await IsActive(token)).Should().BeFalse("the user-level entry is kept for the revocation horizon");
    }
}
