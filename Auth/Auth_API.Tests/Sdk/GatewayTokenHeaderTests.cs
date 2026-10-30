using System.Net;
using Auth.Domain.Services;
using Auth.Sdk;
using Auth.Sdk.Extensions;
using Auth.Sdk.TokenManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// The SDK attached <c>X-Gateway-Token</c> twice — once in the named-client
/// registration and once again in <c>AuthSystemClient.CreateClient</c>.
/// <see cref="IHttpClientFactory"/> re-runs the registration delegate on every
/// <c>CreateClient</c> call, and the header has no registered parser so
/// <c>Add</c> appends instead of replacing: the value reached the wire as
/// "token, token". The API reads it with <c>StringValues.ToString()</c> and
/// compares the whole string, so the length check failed first and every call
/// came back 403.
/// </summary>
/// <remarks>
/// Asserted on the OUTGOING request rather than on DefaultRequestHeaders,
/// because the failure is a property of what is sent. Two calls, not one: the
/// original defect needed the factory to re-run the delegate, so a test that
/// resolves a client once could pass over a reintroduced duplicate.
///
/// This is the first test the SDK has ever had. Both defects survived because
/// Gateway:ValidationEnabled is false in Development and true in Production —
/// the code was only wrong where nobody was running it.
/// </remarks>
public class GatewayTokenHeaderTests
{
    private const string Token = "gateway-token-value";

    /// <summary>Captures every outgoing request and answers without a network.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static (AuthSystemClient Client, CapturingHandler Handler) Build(string token)
    {
        var handler = new CapturingHandler();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = "https://auth.example.com";
            options.GatewayToken = token;
            options.Issuer = "https://auth.example.com";
            options.Audience = "platform";
        });

        // Replace the transport only; the delegating handlers and the
        // registration delegate under test stay exactly as configured.
        services.AddHttpClient(AuthSystemConstants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<AuthSystemClient>(), handler);
    }

    [Fact]
    public async Task MainClient_SendsGatewayTokenExactlyOnce()
    {
        var (client, handler) = Build(Token);

        await client.LoginAsync("user1@example.com", "password", "app");

        var values = handler.Requests.Single()
            .Headers.GetValues(AuthSystemConstants.GatewayTokenHeaderName)
            .ToList();

        values.Should().ContainSingle().Which.Should().Be(Token);
    }

    [Fact]
    public async Task RepeatedCalls_DoNotAccumulateTheHeader()
    {
        // The defect's actual mechanism: one add per CreateClient, forever.
        var (client, handler) = Build(Token);

        await client.LoginAsync("user1@example.com", "password", "app");
        await client.LoginAsync("user2@example.com", "password", "app");

        handler.Requests.Should().HaveCount(2);
        foreach (var request in handler.Requests)
        {
            request.Headers.GetValues(AuthSystemConstants.GatewayTokenHeaderName)
                .Should().ContainSingle().Which.Should().Be(Token);
        }
    }

    /// <summary>
    /// The credential checks' own client (SDK 2.0, <c>AuthSystem.MachineApi</c>) is registered
    /// apart from the main one, so it carries the header by its own delegate and is pinned apart.
    /// </summary>
    private static (AuthSystemClient Client, CapturingHandler Handler) BuildForChecks(string token)
    {
        var handler = new CapturingHandler();
        var machineTokens = new Mock<IMachineTokenProvider>();
        machineTokens
            .Setup(m => m.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("machine-token");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = "https://auth.example.com";
            options.GatewayToken = token;
            options.Issuer = "https://auth.example.com";
            options.Audience = "crm-web";
            options.ClientId = "crm-web";
        });
        services.AddHttpClient(AuthSystemConstants.MachineApiHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton(machineTokens.Object);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<AuthSystemClient>(), handler);
    }

    private static string Key(int n) =>
        ApiKeyFormat.Compose("production", n.ToString("D16"), new string('s', 32));

    [Fact]
    public async Task CredentialChecks_SendTheGatewayTokenExactlyOnce_OnEveryCall()
    {
        var (client, handler) = BuildForChecks(Token);

        // Two keys, so neither answer comes from the SDK's cache.
        await client.ValidateApiKeyAsync(Key(1));
        await client.ValidateApiKeyAsync(Key(2));

        handler.Requests.Should().HaveCount(2);
        foreach (var request in handler.Requests)
        {
            request.Headers.GetValues(AuthSystemConstants.GatewayTokenHeaderName)
                .Should().ContainSingle().Which.Should().Be(Token);
            request.Headers.Authorization!.Parameter.Should().Be("machine-token");
        }
    }

    [Fact]
    public async Task CredentialChecks_WithNoConfiguredToken_SendNoHeaderAtAll()
    {
        var (client, handler) = BuildForChecks(string.Empty);

        await client.ValidateApiKeyAsync(Key(1));

        handler.Requests.Single()
            .Headers.Contains(AuthSystemConstants.GatewayTokenHeaderName)
            .Should().BeFalse();
    }

    [Fact]
    public async Task NoConfiguredToken_SendsNoHeaderAtAll()
    {
        // An empty header is not the same as no header to a validating gateway:
        // it fails the comparison instead of skipping it.
        var (client, handler) = Build(string.Empty);

        await client.LoginAsync("user1@example.com", "password", "app");

        handler.Requests.Single()
            .Headers.Contains(AuthSystemConstants.GatewayTokenHeaderName)
            .Should().BeFalse();
    }
}
