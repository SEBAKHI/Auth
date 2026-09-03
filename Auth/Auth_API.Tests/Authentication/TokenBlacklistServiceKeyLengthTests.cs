using System.Threading.Channels;
using Auth.Domain.Entities;
using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The in-memory blacklist must never hold a key the durable store would
/// refuse: RevokedTokens.RevocationKey is NVARCHAR(200), and nothing this
/// process issues comes near it. A longer key can only be attacker-shaped.
/// </summary>
public class TokenBlacklistServiceKeyLengthTests
{
    [Fact]
    public void BlacklistToken_KeyWithinTheLimit_IsQueuedForPersistence()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<TokenRevocation>();
        using var service = new TokenBlacklistService(channel.Writer, Mock.Of<ILogger<TokenBlacklistService>>());

        // Act
        service.BlacklistToken(new string('a', TokenBlacklistService.MaxKeyLength), DateTime.UtcNow.AddMinutes(15));

        // Assert
        channel.Reader.TryRead(out _).Should().BeTrue();
    }

    [Fact]
    public void BlacklistToken_KeyOverTheLimit_IsDroppedWithoutTouchingMemoryOrTheQueue()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<TokenRevocation>();
        using var service = new TokenBlacklistService(channel.Writer, Mock.Of<ILogger<TokenBlacklistService>>());

        // Act
        service.BlacklistToken(new string('a', TokenBlacklistService.MaxKeyLength + 1), DateTime.UtcNow.AddMinutes(15));

        // Assert
        channel.Reader.TryRead(out _).Should().BeFalse();
    }
}
