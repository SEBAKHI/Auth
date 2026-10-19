using System.Net;
using System.Threading.RateLimiting;
using Auth.Application.Features.Authentication.RevokeToken;
using Auth.Application.Features.Authentication.ServerSignOut;
using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Tests.Authentication.FirstParty;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication.ConfidentialClients;

/// <summary>
/// POST auth/end-session/server as a server-side application meets it (OI-183): through the real
/// <c>AuthController</c> on a test server, the mediator stubbed. What the handler decides is proven
/// by <see cref="ServerSignOutTests"/>; this proves the wire: the form a server sends, the answer it
/// reads, and the budget the call spends.
/// </summary>
public class ServerSignOutEndpointTests
{
    private const string Path = "/api/v1/auth/end-session/server";

    private static readonly Dictionary<string, string> AForm = new()
    {
        ["refresh_token"] = "the-refresh-token",
        ["client_id"] = "crm",
        ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        ["client_assertion"] = "the-assertion",
    };

    [Fact]
    public async Task EndSessionFromServer_WithTheForm_Returns200WithTheSnakeCaseAnswer_NeverCached()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        ServerSignOutCommand? sent = null;
        host.Sender.Setup(s => s.Send(It.IsAny<ServerSignOutCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<ServerSignOutResult>> command, CancellationToken _) => sent = (ServerSignOutCommand)command)
            .ReturnsAsync((ErrorOr<ServerSignOutResult>)new ServerSignOutResult { SsoSessionEnded = true });

        var response = await PostAsync(host, AForm, "203.0.113.20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"sso_session_ended":true}""");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        sent.Should().Be(new ServerSignOutCommand(
            "the-refresh-token", "crm", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer", "the-assertion", "203.0.113.20"));
    }

    [Fact]
    public async Task EndSessionFromServer_WhenTheClientDoesNotAuthenticate_Returns400WithInvalidClient()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        host.Sender.Setup(s => s.Send(It.IsAny<ServerSignOutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<ServerSignOutResult>)AuthErrors.InvalidClient);

        var response = await PostAsync(host, AForm, "203.0.113.21");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.InvalidClient.Code);
    }

    [Fact]
    public async Task EndSessionFromServer_ForATokenThatIsNotTheApplications_Returns400WithInvalidToken()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        host.Sender.Setup(s => s.Send(It.IsAny<ServerSignOutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<ServerSignOutResult>)AuthErrors.InvalidToken);

        var response = await PostAsync(host, AForm, "203.0.113.22");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.InvalidToken.Code);
    }

    [Fact]
    public async Task EndSessionFromServer_ReadsNoCookie_AndNeedsNoOrigin()
    {
        // A server sends neither. And a browser's SSO cookie riding along changes nothing: the
        // command carries no cookie value, so no page can make this call act for its visitor.
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        host.Sender.Setup(s => s.Send(It.IsAny<ServerSignOutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<ServerSignOutResult>)new ServerSignOutResult { SsoSessionEnded = false });

        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new FormUrlEncodedContent(AForm) };
        request.Headers.TryAddWithoutValidation("Cookie", "auth_idp=a-browser-session");
        request.Headers.TryAddWithoutValidation("Origin", Sibling);
        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "no first-party Origin barrier: the proof is the key and the token");
        typeof(ServerSignOutCommand).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            ["RefreshToken", "ClientId", "ClientAssertionType", "ClientAssertion", "IpAddress"]);
    }

    [Fact]
    public async Task EndSessionFromServer_AsJson_Returns415()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);

        var response = await host.Client.PostAsync(Path, new StringContent(
            """{"refresh_token":"x"}""", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ProblemCodeAsync(response)).Should().Be(TransportErrorCodes.UnsupportedMediaType);
        host.Sender.Verify(s => s.Send(It.IsAny<ServerSignOutCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EndSessionFromServer_SpendsTheTokenBudget_TogetherWithRevocation()
    {
        const int TokenLimit = 3;
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false, configureRateLimiter: options =>
        {
            options.AddPolicy("token", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                $"v0:{ClientIpResolver.Resolve(httpContext) ?? "unknown"}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = TokenLimit, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return ValueTask.CompletedTask;
            };
        });
        host.Sender.Setup(s => s.Send(It.IsAny<ServerSignOutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<ServerSignOutResult>)new ServerSignOutResult { SsoSessionEnded = true });
        host.Sender.Setup(s => s.Send(It.IsAny<RevokeTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);

        for (var i = 0; i < TokenLimit - 1; i++)
        {
            (await PostAsync(host, AForm, "203.0.113.23")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var revoke = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "x" })
        };
        revoke.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.23");
        (await host.Client.SendAsync(revoke)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await PostAsync(host, AForm, "203.0.113.23");
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "a server's sign-outs and revocations share one budget");
        (await ProblemCodeAsync(refused)).Should().Be(TransportErrorCodes.RateLimited);
        (await PostAsync(host, AForm, "198.51.100.23")).StatusCode.Should().Be(HttpStatusCode.OK, "per address");
    }

    private static Task<HttpResponseMessage> PostAsync(FirstPartyHost host, Dictionary<string, string> fields, string address)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        return host.Client.SendAsync(request);
    }
}
