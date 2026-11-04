using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// X04, RV3: every "revoke everything" path writes the user-level entry, which the blacklist
/// then keeps until the revocation horizon. The entry is what reaches an access token whose
/// session row is not there to be found; when a session is spared it is not written, because
/// it would kill the spared session's token too.
/// </summary>
/// <remarks>Deliberate break: delete the <c>BlacklistAllUserTokens</c> call in
/// <c>RevokeCredentialsAsync</c>; the first test turns red.</remarks>
public sealed class CredentialRevocationServiceHorizonTests
{
    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly CredentialRevocationService _service;

    public CredentialRevocationServiceHorizonTests()
    {
        _sessions
            .Setup(r => r.GetActiveSessionsForUserAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _service = new CredentialRevocationService(
            _sessions.Object,
            _refreshTokens.Object,
            _idpSessions.Object,
            _blacklist.Object,
            new FakeCredentialHasher(),
            TestHelpers.CreateOptions(new JwtSettings()),
            Mock.Of<IIdpSessionReader>(),
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            new Mock<MediatR.IPublisher>().Object,
            TimeProvider.System,
            NullLogger<CredentialRevocationService>.Instance);
    }

    [Fact]
    public async Task RevokeAllCredentials_WritesTheUserLevelEntry_AtTheInstantOfTheRevocation()
    {
        var userId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        await _service.RevokeAllCredentialsAsync(userId, null, "test", CancellationToken.None);

        _blacklist.Verify(
            b => b.BlacklistAllUserTokens(userId, It.Is<DateTime>(at => at >= before && at <= DateTime.UtcNow)),
            Times.Once);
    }

    [Fact]
    public async Task RevokeCredentials_SparingASession_WritesNoUserLevelEntry()
    {
        var userId = Guid.NewGuid();

        await _service.RevokeCredentialsAsync(userId, Guid.NewGuid(), null, null, "test", CancellationToken.None);

        _blacklist.Verify(b => b.BlacklistAllUserTokens(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
    }
}
