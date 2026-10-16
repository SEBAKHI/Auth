using System.Net;
using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.ClientCredentials;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Infrastructure.Security;
using Auth.Sdk;
using Auth.Sdk.Extensions;
using Auth.Sdk.TokenManagement;
using Auth_API.Tests.Authentication.FirstParty;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The token endpoint's wire contract for the machine grant (OI-128, review F2), through the real
/// <c>AuthController</c> in a test host with the mediator stubbed: which form names bind, what the
/// answer looks like on the wire, and that the other two grants ignore the new fields.
/// </summary>
public class TokenEndpointMachineGrantTests
{
    private const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private static readonly ClientCredentialsTokenResponse MachineToken = new()
    {
        AccessToken = "machine-access-token",
        ExpiresIn = 300,
        Scope = "apikeys.validate"
    };

    private static Func<ClientCredentialsCommand?> CaptureMachineGrant(FirstPartyHost host)
    {
        ClientCredentialsCommand? captured = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<ClientCredentialsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<ClientCredentialsTokenResponse>>, CancellationToken>((command, _) => captured = (ClientCredentialsCommand)command)
            .ReturnsAsync((ErrorOr<ClientCredentialsTokenResponse>)MachineToken);
        return () => captured;
    }

    [Fact]
    public async Task ClientCredentials_BindsEveryFormField_AndAnswersNoStoreWithTheFourOAuthMembers()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        var command = CaptureMachineGrant(host);

        var response = await host.Client.PostAsync("/api/v1/auth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_assertion_type"] = AssertionType,
            ["client_assertion"] = "header.payload.signature",
            ["client_id"] = "shop",
            ["scope"] = "apikeys.validate",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Not CacheControl?.NoStore.Should(): the ?. would skip the assertion when the header is absent.
        (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue("RFC 6749 §5.1: a response that carries a token is never cached");
        var body = await JsonAsync(response);
        body.EnumerateObject().Select(member => member.Name)
            .Should().BeEquivalentTo("access_token", "token_type", "expires_in", "scope");
        body.GetProperty("access_token").GetString().Should().Be("machine-access-token");
        body.GetProperty("token_type").GetString().Should().Be("Bearer");

        command().Should().NotBeNull();
        command()!.ClientAssertionType.Should().Be(AssertionType);
        command()!.ClientAssertion.Should().Be("header.payload.signature");
        command()!.ClientId.Should().Be("shop");
        command()!.Scope.Should().Be("apikeys.validate");
    }

    /// <summary>Forwards the SDK's own request, bytes and headers as sent, to the test host.</summary>
    private sealed class ToTheHost(HttpClient host) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var forwarded = new HttpRequestMessage(request.Method, request.RequestUri!.PathAndQuery)
            {
                Content = new ByteArrayContent(await request.Content!.ReadAsByteArrayAsync(cancellationToken))
            };
            foreach (var header in request.Content.Headers)
            {
                forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return await host.SendAsync(forwarded, cancellationToken);
        }
    }

    [Fact]
    public async Task TheSdksOwnTokenRequest_ReachesTheControllerIntact()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        var command = CaptureMachineGrant(host);
        var key = new MachineKeyMaterial().Generate();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = "https://auth.example.com";
            options.Issuer = "https://auth.example.com";
            options.Audience = "shop";
            options.ClientId = "shop";
            options.MachineKeyId = key.PublicKey.Kid;
            options.MachinePrivateKeyPem = key.PrivateKeyPem;
        });
        services.AddHttpClient(AuthSystemConstants.MachineHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new ToTheHost(host.Client));
        using var provider = services.BuildServiceProvider();

        var token = await provider.GetRequiredService<IMachineTokenProvider>().GetAccessTokenAsync();

        token.Should().Be("machine-access-token");
        command().Should().NotBeNull("the SDK's form names are the controller's");
        command()!.ClientAssertionType.Should().Be(AssertionType);
        command()!.ClientAssertion.Should().NotBeNullOrEmpty().And.Contain(".");
        command()!.ClientId.Should().Be("shop");
    }

    private static readonly Dictionary<string, string> AssertionFields = new()
    {
        ["client_assertion_type"] = AssertionType,
        ["client_assertion"] = "header.payload.signature",
        ["scope"] = "apikeys.validate",
    };

    private static FormUrlEncodedContent Form(Dictionary<string, string> fields, bool withAssertion) =>
        new(withAssertion ? fields.Concat(AssertionFields) : fields);

    [Fact]
    public async Task TheAuthorizationCodeGrant_CarriesTheClientAssertion_AndIsNeverCached()
    {
        // OI-171 (CA1): the assertion is no longer ignored on the user grants. The controller hands
        // it to the code exchange, which authenticates it; it never reaches client_credentials.
        // B8: an answer that carries a token is never cached (RFC 6749 §5.1).
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        var commands = new List<ExchangeAuthorizationCodeCommand>();
        host.Sender
            .Setup(s => s.Send(It.IsAny<ExchangeAuthorizationCodeCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<OAuthTokenResponse>>, CancellationToken>((command, _) => commands.Add((ExchangeAuthorizationCodeCommand)command))
            .ReturnsAsync((ErrorOr<OAuthTokenResponse>)new OAuthTokenResponse
            {
                AccessToken = "user-access-token",
                ExpiresIn = 900,
                RefreshToken = "user-refresh-token",
                RefreshExpiresIn = 604800,
                Scope = "openid"
            });
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "the-code",
            ["redirect_uri"] = "https://app.example.com/callback",
            ["client_id"] = "shop",
            ["code_verifier"] = new string('v', 43),
        };

        var plain = await host.Client.PostAsync("/api/v1/auth/token", Form(fields, withAssertion: false));
        var withAssertion = await host.Client.PostAsync("/api/v1/auth/token", Form(fields, withAssertion: true));

        commands.Should().HaveCount(2);
        commands[0].ClientAssertionType.Should().BeNull();
        commands[0].ClientAssertion.Should().BeNull();
        commands[1].ClientAssertionType.Should().Be(AssertionType);
        commands[1].ClientAssertion.Should().Be("header.payload.signature");
        commands[1].Should().Be(commands[0] with { ClientAssertionType = AssertionType, ClientAssertion = "header.payload.signature" },
            "nothing else of the grant changes");
        foreach (var response in new[] { plain, withAssertion })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue("RFC 6749 §5.1: a response that carries a token is never cached");
            response.Headers.Pragma.Should().ContainSingle().Which.Name.Should().Be("no-cache");
        }

        host.Sender.Verify(s => s.Send(It.IsAny<ClientCredentialsCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheRefreshTokenGrant_CarriesTheClientAssertion_AndIsNeverCached()
    {
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        var commands = new List<RefreshTokenCommand>();
        host.Sender
            .Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<TokenResponse>>, CancellationToken>((command, _) => commands.Add((RefreshTokenCommand)command))
            .ReturnsAsync((ErrorOr<TokenResponse>)Tokens());
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = "r",
            ["client_id"] = "shop",
        };

        var plain = await host.Client.PostAsync("/api/v1/auth/token", Form(fields, withAssertion: false));
        var withAssertion = await host.Client.PostAsync("/api/v1/auth/token", Form(fields, withAssertion: true));

        commands.Should().HaveCount(2);
        commands[0].ClientAssertion.Should().BeNull();
        commands[1].Should().Be(commands[0] with { ClientAssertionType = AssertionType, ClientAssertion = "header.payload.signature" });
        foreach (var response in new[] { plain, withAssertion })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue("RFC 6749 §5.1: a response that carries a token is never cached");
            response.Headers.Pragma.Should().ContainSingle().Which.Name.Should().Be("no-cache");
        }

        host.Sender.Verify(s => s.Send(It.IsAny<ClientCredentialsCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARefusedGrant_CarriesNoCacheHeaders()
    {
        // The header is on the answers that carry a token, not stamped on every answer of the action.
        await using var host = await StartAsync([ConsoleApp], cookieEnabled: false);
        host.Sender
            .Setup(s => s.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<TokenResponse>)Auth.Domain.Errors.AuthErrors.InvalidClient);

        var refused = await host.Client.PostAsync("/api/v1/auth/token", Form(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = "r",
        }, withAssertion: false));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refused.Headers.Pragma.Should().BeEmpty();
    }
}
