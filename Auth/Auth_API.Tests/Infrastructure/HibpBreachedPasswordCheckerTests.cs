using System.Net;
using System.Security.Cryptography;
using System.Text;
using Auth.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Tests for the HIBP Pwned Passwords k-anonymity parsing: only the SHA-1 prefix is requested and
/// the breach count is read from the matching suffix in the response.
/// </summary>
public class HibpBreachedPasswordCheckerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string? RequestedPath { get; private set; }
        public bool HadPaddingHeader { get; private set; }

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPath = request.RequestUri?.AbsolutePath;
            HadPaddingHeader = request.Headers.Contains("Add-Padding");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body)
            });
        }
    }

    private static (string Prefix, string Suffix) Sha1HexParts(string password)
    {
        var hex = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        return (hex[..5], hex[5..]);
    }

    private static HibpBreachedPasswordChecker CreateChecker(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.pwnedpasswords.com/") };
        return new HibpBreachedPasswordChecker(client, NullLogger<HibpBreachedPasswordChecker>.Instance);
    }

    [Fact]
    public async Task GetBreachCountAsync_MatchingSuffix_ReturnsCount_AndQueriesPrefixWithPadding()
    {
        const string password = "Password1!";
        var (prefix, suffix) = Sha1HexParts(password);

        var body = $"0000000000000000000000000000000000A:5\r\n{suffix}:1234\r\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:9";
        var handler = new StubHandler(body);
        var checker = CreateChecker(handler);

        var count = await checker.GetBreachCountAsync(password, CancellationToken.None);

        count.Should().Be(1234);
        handler.RequestedPath.Should().Be($"/range/{prefix}");
        handler.HadPaddingHeader.Should().BeTrue();
    }

    [Fact]
    public async Task GetBreachCountAsync_NoMatchingSuffix_ReturnsZero()
    {
        const string password = "a-very-unique-passphrase-not-in-list";

        var body = "0000000000000000000000000000000000A:5\r\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:9";
        var handler = new StubHandler(body);
        var checker = CreateChecker(handler);

        var count = await checker.GetBreachCountAsync(password, CancellationToken.None);

        count.Should().Be(0);
    }
}
