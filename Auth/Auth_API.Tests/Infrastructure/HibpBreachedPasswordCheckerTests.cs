using System.Net;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using Auth.Application.Configuration;
using Auth.Infrastructure.Security;

using Auth_API.Tests.Helpers;

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
        return new HibpBreachedPasswordChecker(client, Settings(new PasswordSettings()));
    }

    private static TestHelpers.TestOptions<PasswordSettings> Settings(PasswordSettings settings) =>
        TestHelpers.CreateOptions(settings);

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

    /// <summary>
    /// A success answer with no range listing in it (a TLS-inspection block page, a captive
    /// portal, an empty body) is not "not breached": it throws, so the evaluator follows FailOpen
    /// as "(Error)" with a Warning, or refuses when fail-closed. HIBP itself always answers with
    /// hundreds of padded entries.
    /// </summary>
    [Theory]
    [InlineData("<html><body>Blocked by policy</body></html>", "text/html")]
    [InlineData("", "text/plain")]
    [InlineData("not:a listing\nstill:nothing", "text/plain")]
    public async Task SuccessWithNoListing_Throws(string body, string mediaType)
    {
        var handler = new StaticResponseHttpMessageHandler(HttpStatusCode.OK, body, mediaType);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.pwnedpasswords.com/") };
        var checker = new HibpBreachedPasswordChecker(client, Settings(new PasswordSettings()));

        var act = () => checker.GetBreachCountAsync("Password1!", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().Be(1);
    }

    /// <summary>
    /// Pins the shape the evaluator's fail-open decision rests on: when HttpClient.Timeout elapses,
    /// the checker lets the runtime's exception through unchanged, an OperationCanceledException
    /// whose inner exception is a TimeoutException, while the caller's token is not cancelled.
    /// </summary>
    [Fact]
    public async Task GetBreachCountAsync_Timeout_ThrowsTimeoutInner()
    {
        var handler = new HangingHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        var checker = new HibpBreachedPasswordChecker(client, Settings(new PasswordSettings()));
        using var caller = new CancellationTokenSource();

        var act = () => checker.GetBreachCountAsync("Password1!", caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().WithInnerException(typeof(TimeoutException));
        caller.IsCancellationRequested.Should().BeFalse();
        handler.Requests.Should().Be(1);
    }

    /// <summary>
    /// TimeoutMs is hot (S09): one checker instance, as the typed client keeps it, applies the value
    /// in force at each call. The deadline is the checker's own, under the client's 30 s ceiling,
    /// and it ends the call as a plain cancellation the caller did not request.
    /// </summary>
    [Fact]
    public async Task TimeoutMs_ReadPerCall()
    {
        var handler = new HangingHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
            Timeout = TimeSpan.FromMilliseconds(BreachedPasswordCheckSettings.MaxTimeoutMs),
        };
        var settings = new PasswordSettings();
        settings.BreachedPasswordCheck.TimeoutMs = 5000;
        var checker = new HibpBreachedPasswordChecker(client, Settings(settings));

        // The first call is abandoned by its caller well before 5000 ms; only the second one counts.
        using (var first = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            var abandoned = () => checker.GetBreachCountAsync("Password1!", first.Token);
            await abandoned.Should().ThrowAsync<OperationCanceledException>();
        }

        settings.BreachedPasswordCheck.TimeoutMs = 100;
        using var caller = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        var act = () => checker.GetBreachCountAsync("Password1!", caller.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        thrown.Which.InnerException.Should().NotBeOfType<TimeoutException>(
            "the deadline is the checker's, not HttpClient.Timeout");
        caller.IsCancellationRequested.Should().BeFalse();
        handler.Requests.Should().Be(2);
    }

    /// <summary>
    /// The caller's cancellation reaches the request through the deadline's linked token, so a
    /// client that goes away ends the call at once, not when TimeoutMs runs out, and the
    /// cancellation propagates as itself.
    /// </summary>
    [Fact]
    public async Task CallerCancelled_Propagates()
    {
        var handler = new HangingHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
            Timeout = TimeSpan.FromMilliseconds(BreachedPasswordCheckSettings.MaxTimeoutMs),
        };
        var settings = new PasswordSettings();
        settings.BreachedPasswordCheck.TimeoutMs = BreachedPasswordCheckSettings.MaxTimeoutMs;
        var checker = new HibpBreachedPasswordChecker(client, Settings(settings));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        var act = () => checker.GetBreachCountAsync("Password1!", caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        caller.IsCancellationRequested.Should().BeTrue();
        handler.Requests.Should().Be(1);
    }
}
