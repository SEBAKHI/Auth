using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Auth.Shared.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth_API.Tests.Gateway;

/// <summary>
/// The edge half of the token budget per application (OI-178), as a server meets it: the real
/// <see cref="ClientAssertionPresenceMiddleware"/> in front of a limiter built the way the gateway's
/// Program.cs builds its global bucket and its "token" policy, and an upstream that reads the body
/// it is forwarded. This project holds no reference to the gateway process; its Program.cs wiring
/// is pinned by <see cref="Configuration.TokenBudgetPerApplicationGuardTests"/>.
/// </summary>
public sealed class TokenAssertionAtTheEdgeTests : IAsyncLifetime
{
    private const string TokenPath = "/api/v1/auth/token";
    private const int Global = 4;
    private const int PerAddress = 2;
    private const int PerAssertingAddress = 10;

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddRateLimiter(options =>
                    {
                        // As the gateway's Program.cs builds them, with tiny limits.
                        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                            ClientAssertionPresenceMiddleware.IsMarked(context)
                                ? RateLimitPartition.GetNoLimiter("token-assertion")
                                : RateLimitPartition.GetFixedWindowLimiter(
                                    $"v0:{context.Connection.RemoteIpAddress}", _ => Window(Global)));
                        options.AddPolicy("token", context => !ClientAssertionPresenceMiddleware.IsMarked(context)
                            ? RateLimitPartition.GetFixedWindowLimiter($"v0:{context.Connection.RemoteIpAddress}", _ => Window(PerAddress))
                            : RateLimitPartition.GetFixedWindowLimiter($"v0:assertion:{context.Connection.RemoteIpAddress}", _ => Window(PerAssertingAddress)));
                        options.OnRejected = (context, _) =>
                        {
                            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                            return ValueTask.CompletedTask;
                        };
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseMiddleware<ClientAssertionPresenceMiddleware>(
                        (Func<HttpContext, bool>)(context => context.Request.Path == TokenPath));
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                    {
                        // The upstream: what YARP forwards is the request body as it stands now.
                        endpoints.MapPost(TokenPath, async (HttpContext context) =>
                        {
                            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                            var forwarded = await reader.ReadToEndAsync();
                            return Results.Text($"{ClientAssertionPresenceMiddleware.IsMarked(context)}|{forwarded}");
                        }).RequireRateLimiting("token");
                        endpoints.MapPost("/api/v1/auth/login", () => Results.Ok());
                    });
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private static FixedWindowRateLimiterOptions Window(int permits) =>
        new() { PermitLimit = permits, Window = TimeSpan.FromHours(1), QueueLimit = 0 };

    private static FormUrlEncodedContent Form(bool withAssertion, int padding = 0)
    {
        var fields = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = "r-token" };
        if (withAssertion)
        {
            fields["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
            fields["client_assertion"] = "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJzaG9wIn0.c2ln";
        }

        if (padding > 0)
        {
            fields["padding"] = new string('p', padding);
        }

        return new FormUrlEncodedContent(fields);
    }

    // ===== TB5: marked, counted apart, forwarded intact =====

    [Fact]
    public async Task TB5_ARequestWithAnAssertion_IsMarked_AndForwardedExactlyAsItArrived()
    {
        var sent = await Form(withAssertion: true).ReadAsStringAsync();

        var response = await _client.PostAsync(TokenPath, Form(withAssertion: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be($"True|{sent}",
            "the form was read to look for the assertion, then rewound: the upstream gets every byte");
    }

    [Fact]
    public async Task TB5_ARequestWithoutAnAssertion_IsNotMarked_AndForwardedIntact()
    {
        var sent = await Form(withAssertion: false).ReadAsStringAsync();

        var response = await _client.PostAsync(TokenPath, Form(withAssertion: false));

        (await response.Content.ReadAsStringAsync()).Should().Be($"False|{sent}");
    }

    [Fact]
    public async Task TB5_AnOversizedForm_IsLeftUnmarked_AndForwardedIntact()
    {
        var oversized = Form(withAssertion: true, padding: ClientAssertionPresenceMiddleware.MaxFormBytes);
        var sent = await oversized.ReadAsStringAsync();

        var response = await _client.PostAsync(TokenPath, oversized);

        (await response.Content.ReadAsStringAsync()).Should().Be($"False|{sent}",
            "beyond the bound the form is not read at all, and counts as before");
    }

    [Fact]
    public async Task TB5_AFormTheReaderRefuses_IsLeftUnmarked_AndForwardedIntact()
    {
        // More fields than the form reader accepts (1,024), within the size bound: the read throws
        // part-way, and the body is still rewound for the upstream.
        var fields = Enumerable.Range(0, 1100).ToDictionary(i => $"f{i}", _ => "1");
        fields["client_assertion"] = "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJzaG9wIn0.c2ln";
        var content = new FormUrlEncodedContent(fields);
        var sent = await content.ReadAsStringAsync();

        var response = await _client.PostAsync(TokenPath, content);

        (await response.Content.ReadAsStringAsync()).Should().Be($"False|{sent}");
    }

    [Fact]
    public async Task TB5_AssertionBearingRequests_HaveTheirOwnCeiling_OutsideTheGlobalBucket()
    {
        for (var i = 0; i < PerAssertingAddress; i++)
        {
            (await _client.PostAsync(TokenPath, Form(withAssertion: true))).StatusCode.Should().Be(HttpStatusCode.OK,
                "request {0} is within the assertion ceiling, past both the per-address token limit and the global bucket", i + 1);
        }

        (await _client.PostAsync(TokenPath, Form(withAssertion: true))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the assertion ceiling still binds");
        (await _client.PostAsync("/api/v1/auth/login", Form(withAssertion: false))).StatusCode.Should().Be(HttpStatusCode.OK,
            "assertion-bearing token requests took nothing from the global bucket every other route shares");
    }

    [Fact]
    public async Task TB5_RequestsWithoutAnAssertion_KeepThePerAddressTokenLimit()
    {
        for (var i = 0; i < PerAddress; i++)
        {
            (await _client.PostAsync(TokenPath, Form(withAssertion: false))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await _client.PostAsync(TokenPath, Form(withAssertion: false))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the token policy's per-address limit, with the global bucket not yet spent");
    }

    [Fact]
    public async Task TB5_RequestsWithoutAnAssertion_AreStillCountedByTheGlobalBucket()
    {
        for (var i = 0; i < PerAddress; i++)
        {
            (await _client.PostAsync(TokenPath, Form(withAssertion: false))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        for (var i = PerAddress; i < Global; i++)
        {
            (await _client.PostAsync("/api/v1/auth/login", Form(withAssertion: false))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await _client.PostAsync("/api/v1/auth/login", Form(withAssertion: false))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the two token requests without an assertion spent the global bucket, as every request did before");
    }
}
