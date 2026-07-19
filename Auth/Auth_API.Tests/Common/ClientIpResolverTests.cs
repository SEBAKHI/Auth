using Auth_API.Common;
using Microsoft.AspNetCore.Http;

namespace Auth_API.Tests.Common;

/// <summary>
/// Unit tests for ClientIpResolver — the shared client-IP source used by both
/// audit logging and per-client rate-limit partitioning.
/// </summary>
public class ClientIpResolverTests
{
    [Fact]
    public void Resolve_WithForwardedFor_ReturnsFirstEntry()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 192.250.231.24";

        ClientIpResolver.Resolve(context).Should().Be("203.0.113.7");
    }

    [Fact]
    public void Resolve_WithForwardedFor_TrimsWhitespace()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "  203.0.113.7  ";

        ClientIpResolver.Resolve(context).Should().Be("203.0.113.7");
    }

    [Fact]
    public void Resolve_WithoutForwardedFor_FallsBackToConnectionAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.9");

        ClientIpResolver.Resolve(context).Should().Be("198.51.100.9");
    }

    [Fact]
    public void Resolve_DistinctForwardedFor_ProduceDistinctKeys()
    {
        // The property the rate-limiter partitioning relies on: two different
        // real clients behind the same gateway peer get different partition keys.
        var a = new DefaultHttpContext();
        a.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 192.250.231.24";
        a.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.250.231.24");

        var b = new DefaultHttpContext();
        b.Request.Headers["X-Forwarded-For"] = "203.0.113.99, 192.250.231.24";
        b.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.250.231.24");

        ClientIpResolver.Resolve(a).Should().NotBe(ClientIpResolver.Resolve(b));
    }
}
