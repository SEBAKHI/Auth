using System.Net;
using System.Text;
using Auth.Sdk;
using Auth.Sdk.Extensions;
using Auth.Sdk.TokenManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Auth_API.Tests.Sdk;

/// <summary>
/// SDK 2.1 (S12, TS3): <c>IntrospectTokenAsync</c> calls a token active only when it was issued to
/// this application, whatever the answer says: <c>aud</c> for an access token, <c>client_id</c> for
/// a refresh token, which carries no <c>aud</c>. The AuthSystem already answers so; this keeps an
/// application from taking another application's token for its own on the word of one check.
/// </summary>
public sealed class AuthSystemClientIntrospectTests : IDisposable
{
    private const string ThisApplication = "CRM";

    private readonly Introspection _authSystem = new();
    private readonly ServiceProvider _provider;

    public AuthSystemClientIntrospectTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthSystemAuthentication(options =>
        {
            options.BaseUrl = "https://auth.example.com";
            options.Issuer = "https://auth.example.com";
            options.Audience = ThisApplication;
            options.ClientId = ThisApplication;
            options.MachineKeyId = "kid-1";
            options.MachinePrivateKeyPem = "stub: the provider is replaced";
        });
        services.AddHttpClient(AuthSystemConstants.MachineApiHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => _authSystem);
        services.AddSingleton<IMachineTokenProvider>(new FixedMachineToken());
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private AuthSystemClient Sdk => _provider.GetRequiredService<AuthSystemClient>();

    [Theory]
    [InlineData("""{"active":true,"aud":"OTHER-APP","client_id":"OTHER-APP"}""")]
    [InlineData("""{"active":true,"aud":"OTHER-APP"}""")]
    [InlineData("""{"active":true,"aud":"CRM","client_id":"OTHER-APP"}""")]
    [InlineData("""{"active":true,"client_id":"OTHER-APP"}""")]
    [InlineData("""{"active":true}""")]
    public async Task AnActiveAnswer_AboutATokenNotIssuedToThisApplication_IsInactive(string answer)
    {
        _authSystem.Body = answer;

        var result = await Sdk.IntrospectTokenAsync("a.token.value");

        result.Should().NotBeNull();
        result!.Active.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"active":true,"token_type":"bearer","aud":"CRM","client_id":"CRM"}""")]
    [InlineData("""{"active":true,"token_type":"bearer","aud":"crm","client_id":"crm"}""")]
    [InlineData("""{"active":true,"token_type":"refresh_token","client_id":"CRM"}""")]
    public async Task AnActiveAnswer_AboutThisApplicationsToken_StaysActive(string answer)
    {
        _authSystem.Body = answer;

        var result = await Sdk.IntrospectTokenAsync("a.token.value");

        result!.Active.Should().BeTrue();
    }

    [Fact]
    public async Task ATokenTurnedInactiveHere_IsHeldLikeAnyInactiveAnswer()
    {
        _authSystem.Body = """{"active":true,"aud":"OTHER-APP","client_id":"OTHER-APP"}""";

        await Sdk.IntrospectTokenAsync("a.token.value");
        (await Sdk.IntrospectTokenAsync("a.token.value"))!.Active.Should().BeFalse();

        _authSystem.Calls.Should().Be(1, "a token issued to another application never becomes this one's");
    }

    private sealed class Introspection : HttpMessageHandler
    {
        public string Body { get; set; } = """{"active":false}""";

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            request.RequestUri!.AbsolutePath.Should().EndWith("tokens/introspect");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FixedMachineToken : IMachineTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("machine-1");

        public void Invalidate(string accessToken)
        {
        }
    }
}
