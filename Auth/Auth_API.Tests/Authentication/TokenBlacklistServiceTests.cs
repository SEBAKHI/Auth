using System.Threading.Channels;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for TokenBlacklistService — the in-memory read cache plus the
/// write-behind + rehydration mechanism that make revocation survive restarts.
/// </summary>
public class TokenBlacklistServiceTests
{
    private static (TokenBlacklistService Service, Channel<TokenRevocation> Queue) CreateService()
    {
        var channel = Channel.CreateUnbounded<TokenRevocation>();
        var service = new TokenBlacklistService(
            channel.Writer, new Mock<ILogger<TokenBlacklistService>>().Object);
        return (service, channel);
    }

    private static List<TokenRevocation> Drain(Channel<TokenRevocation> channel)
    {
        var items = new List<TokenRevocation>();
        while (channel.Reader.TryRead(out var item))
        {
            items.Add(item);
        }
        return items;
    }

    [Fact]
    public void BlacklistToken_MarksInMemoryAndEnqueuesForPersistence()
    {
        var (service, channel) = CreateService();
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        service.BlacklistToken("jti-1", expiresAt);

        service.IsTokenBlacklisted("jti-1").Should().BeTrue();
        var queued = Drain(channel);
        queued.Should().ContainSingle(r => r.Type == RevocationType.Token && r.Key == "jti-1");
    }

    [Fact]
    public void BlacklistSession_MarksInMemoryAndEnqueues()
    {
        var (service, channel) = CreateService();

        service.BlacklistSession("sid-1", DateTime.UtcNow.AddMinutes(15));

        service.IsSessionBlacklisted("sid-1").Should().BeTrue();
        Drain(channel).Should().ContainSingle(r => r.Type == RevocationType.Session && r.Key == "sid-1");
    }

    [Fact]
    public void BlacklistAllUserTokens_RejectsTokensIssuedBeforeRevocation()
    {
        var (service, channel) = CreateService();
        var revokedAt = DateTime.UtcNow;

        service.BlacklistAllUserTokens(Guid.Parse("11111111-1111-1111-1111-111111111111"), revokedAt);

        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        service.AreUserTokensBlacklisted(userId, revokedAt.AddSeconds(-1)).Should().BeTrue();
        service.AreUserTokensBlacklisted(userId, revokedAt.AddSeconds(1)).Should().BeFalse();
        Drain(channel).Should().ContainSingle(r => r.Type == RevocationType.User);
    }

    [Fact]
    public void LoadSnapshot_RehydratesAllRevocationTypesAfterRestart()
    {
        // Simulate: writes happened, were persisted, then the process recycled
        // (fresh empty service). Rehydration from the durable snapshot must
        // restore every revocation.
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var revokedAt = DateTime.UtcNow;
        var future = DateTime.UtcNow.AddMinutes(15);

        var snapshot = new[]
        {
            new TokenRevocation(RevocationType.Token, "jti-9", DateTime.UtcNow, future),
            new TokenRevocation(RevocationType.Session, "sid-9", DateTime.UtcNow, future),
            new TokenRevocation(RevocationType.User, userId.ToString(), revokedAt, revokedAt.AddHours(1)),
        };

        var (freshService, _) = CreateService();
        freshService.LoadSnapshot(snapshot);

        freshService.IsTokenBlacklisted("jti-9").Should().BeTrue();
        freshService.IsSessionBlacklisted("sid-9").Should().BeTrue();
        freshService.AreUserTokensBlacklisted(userId, revokedAt.AddSeconds(-1)).Should().BeTrue();
    }

    [Fact]
    public void LoadSnapshot_IgnoresExpiredEntries()
    {
        var (service, _) = CreateService();
        var expired = new[]
        {
            new TokenRevocation(RevocationType.Token, "jti-old", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1)),
        };

        service.LoadSnapshot(expired);

        service.IsTokenBlacklisted("jti-old").Should().BeFalse();
    }
}
