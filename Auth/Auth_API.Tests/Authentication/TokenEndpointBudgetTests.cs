using System.Net;
using System.Threading.RateLimiting;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.RevokeToken;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Tests.Authentication.FirstParty;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The token budget as a client meets it (OI-171): requests through the real
/// <c>AuthController</c> on a test server, with the limiter in the pipeline, counted by the
/// policy each action names.
/// </summary>
/// <remarks>
/// What this proves and the source guards cannot: that POST auth/token is refused at the
/// token budget's own limit (TA1), that spending either budget leaves the other whole (TA2),
/// and that POST auth/revoke spends the token budget (TA3) — whatever attribute text puts
/// them there, including a <c>[DisableRateLimiting]</c> or a policy declared on the controller.
///
/// <para>
/// The two policies are registered here with tiny limits and partitioned the way Program.cs
/// partitions them: the settings-version stamp and <see cref="ClientIpResolver"/>. The policy
/// bodies in Program.cs themselves — which settings they read, their defaults, their key — stay
/// pinned by <see cref="Configuration.TokenEndpointThrottleGuardTests"/>, which reads that file.
/// </para>
/// </remarks>
public class TokenEndpointBudgetTests
{
    private const int SignInLimit = 2;
    private const int TokenLimit = 3;

    // A window no test outlives: a refusal here is the limit, never a clock edge.
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    [Fact]
    public async Task TheTokenEndpoint_IsRefusedAtTheTokenLimit_WithTheRateLimitedProblem()
    {
        await using var host = await StartThrottledAsync();

        for (var i = 0; i < TokenLimit; i++)
        {
            (await TokenAsync(host, "203.0.113.10")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                "request {0} of {1} is within the token budget", i + 1, TokenLimit);
        }

        var refused = await TokenAsync(host, "203.0.113.10");

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ProblemCodeAsync(refused)).Should().Be(TransportErrorCodes.RateLimited);
        refused.Headers.RetryAfter.Should().NotBeNull("a server renewing its users' tokens must know when to try again");
    }

    [Fact]
    public async Task TheTokenEndpoint_IsNotHeldToTheSignInLimit()
    {
        await using var host = await StartThrottledAsync();

        // More than the sign-in limit, within the token one: refused here is the defect itself.
        for (var i = 0; i < TokenLimit; i++)
        {
            (await TokenAsync(host, "203.0.113.11")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task SpendingTheTokenBudget_LeavesTheSignInBudgetWhole()
    {
        await using var host = await StartThrottledAsync();
        await SpendTokenBudgetAsync(host, "203.0.113.12");

        for (var i = 0; i < SignInLimit; i++)
        {
            (await SignInAsync(host, "203.0.113.12")).StatusCode.Should().Be(HttpStatusCode.OK,
                "a server's renewals must never refuse a person's sign-in from the same address");
        }

        (await SignInAsync(host, "203.0.113.12")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the sign-in bucket keeps its own limit");
    }

    [Fact]
    public async Task SpendingTheSignInBudget_LeavesTheTokenBudgetWhole()
    {
        await using var host = await StartThrottledAsync();
        for (var i = 0; i < SignInLimit; i++)
        {
            await SignInAsync(host, "203.0.113.13");
        }

        (await SignInAsync(host, "203.0.113.13")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        for (var i = 0; i < TokenLimit; i++)
        {
            (await TokenAsync(host, "203.0.113.13")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                "password guessing from an address must not sign out the people a server there renews for");
        }
    }

    [Fact]
    public async Task Revocation_SpendsTheTokenBudget()
    {
        await using var host = await StartThrottledAsync();

        for (var i = 0; i < TokenLimit; i++)
        {
            (await RevokeAsync(host, "203.0.113.14")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await RevokeAsync(host, "203.0.113.14")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "revocation is anonymous and writes per call, so it is bounded, by the token budget");
        (await TokenAsync(host, "203.0.113.14")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "one budget: a server's sign-outs and renewals are counted together");
        (await SignInAsync(host, "203.0.113.14")).StatusCode.Should().Be(HttpStatusCode.OK,
            "revocation never spends the sign-in budget");
    }

    [Fact]
    public async Task TheTokenBudget_IsPerClientAddress()
    {
        await using var host = await StartThrottledAsync();
        await SpendTokenBudgetAsync(host, "203.0.113.15");

        (await TokenAsync(host, "198.51.100.15")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "one address cannot spend another's budget");
    }

    #region Helpers

    private static async Task<FirstPartyHost> StartThrottledAsync()
    {
        var host = await StartAsync([ConsoleApp], cookieEnabled: false, configureRateLimiter: options =>
        {
            options.AddPolicy("login", httpContext => PerAddress(httpContext, SignInLimit));
            options.AddPolicy("token", httpContext => PerAddress(httpContext, TokenLimit));

            // As Program.cs answers a refusal: the status and Retry-After, no body of its own;
            // the error contract's status-code pages write the problem.
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
        });

        host.Sender.Setup(s => s.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<LoginResponse>)SignIn());
        host.Sender.Setup(s => s.Send(It.IsAny<RevokeTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);
        return host;
    }

    /// <summary>The partition Program.cs builds for both policies: settings version, then client address.</summary>
    private static RateLimitPartition<string> PerAddress(HttpContext httpContext, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"v0:{ClientIpResolver.Resolve(httpContext) ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = Window,
                QueueLimit = 0
            });

    private static async Task SpendTokenBudgetAsync(FirstPartyHost host, string address)
    {
        for (var i = 0; i < TokenLimit; i++)
        {
            await TokenAsync(host, address);
        }

        (await TokenAsync(host, address)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the token budget is spent");
    }

    /// <summary>
    /// A token request the controller answers without the mediator (an unsupported grant, 400):
    /// the limiter counts it before the action runs, as it counts every request.
    /// </summary>
    private static Task<HttpResponseMessage> TokenAsync(FirstPartyHost host, string address) =>
        FormAsync(host, "/api/v1/auth/token", address, new() { ["grant_type"] = "unknown_grant" });

    private static Task<HttpResponseMessage> RevokeAsync(FirstPartyHost host, string address) =>
        FormAsync(host, "/api/v1/auth/revoke", address, new() { ["token"] = "any-token" });

    private static Task<HttpResponseMessage> SignInAsync(FirstPartyHost host, string address)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                """{"email":"a@example.com","password":"a-password"}""", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Origin", ConsoleApp);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        return host.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> FormAsync(
        FirstPartyHost host, string path, string address, Dictionary<string, string> fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        return host.Client.SendAsync(request);
    }

    #endregion
}
