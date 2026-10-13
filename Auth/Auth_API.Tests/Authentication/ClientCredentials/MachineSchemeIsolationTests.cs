using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Auth.Application.Interfaces;
using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth_API.Common.Authentication;
using Auth_API.Common.Middleware;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// The machine scheme and the two older schemes keep each other's tokens out (OI-128, T4), in both
/// directions: on the validation profiles themselves, and on a real pipeline with the real JwtBearer
/// handlers and the blacklist middleware in Program.cs's order. Tokens are real: minted by the token
/// service or signed with its key.
/// </summary>
public sealed class MachineSchemeIsolationTests : IAsyncLifetime
{
    private readonly UserInfoTokens _tokens = new();
    private readonly User _user = TestHelpers.CreateUser();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private IHost _host = null!;
    private HttpClient _client = null!;

    private TokenValidationParameters MachineParameters => AccessTokenValidation.Machine(_tokens.Settings, _tokens.Key);

    private string MachineToken() => _tokens.Service.GenerateMachineAccessToken("shop", MachineClients.Scopes.All);

    /// <summary>A machine-shaped token the service never mints, signed with the same key.</summary>
    private string CustomMachineToken(Action<SecurityTokenDescriptor> customize) =>
        _tokens.Custom(Guid.Empty, descriptor =>
        {
            descriptor.TokenType = "at+jwt";
            descriptor.Audience = MachineClients.Audience;
            descriptor.Claims["sub"] = "shop";
            descriptor.Claims["client_id"] = "shop";
            descriptor.Claims["scope"] = "apikeys.validate";
            customize(descriptor);
        });

    private async Task<bool> PassesAsync(string token, TokenValidationParameters parameters) =>
        (await UserInfoTokens.ValidateAsync(token, parameters)).IsValid;

    // --- the profiles ---

    [Fact]
    public async Task MachineProfile_AcceptsAMachineToken()
    {
        (await PassesAsync(MachineToken(), MachineParameters)).Should().BeTrue();
    }

    [Fact]
    public async Task MachineProfile_RefusesPlatformAndApplicationTokens()
    {
        (await PassesAsync(_tokens.ForPlatform(_user), MachineParameters)).Should().BeFalse();
        (await PassesAsync(_tokens.ForApplication(_user, "openid"), MachineParameters)).Should().BeFalse();
    }

    [Fact]
    public async Task PlatformAndUserInfoProfiles_RefuseAMachineToken()
    {
        var token = MachineToken();

        (await PassesAsync(token, _tokens.PlatformParameters)).Should().BeFalse();
        (await PassesAsync(token, _tokens.UserInfoParameters)).Should().BeFalse();
    }

    [Fact]
    public async Task MachineProfile_RefusesATokenWithoutClientId()
    {
        var token = CustomMachineToken(d => d.Claims.Remove("client_id"));

        (await PassesAsync(token, MachineParameters)).Should().BeFalse();
    }

    [Fact]
    public async Task MachineProfile_RefusesAClientIdThatIsNotTheSubject()
    {
        var token = CustomMachineToken(d => d.Claims["client_id"] = "other-app");

        (await PassesAsync(token, MachineParameters)).Should().BeFalse();
    }

    [Fact]
    public async Task MachineProfile_RefusesAGuidShapedSubject()
    {
        // F1: readers that parse sub as a user id must never meet a machine token. The control
        // shows the same token shape passes with a code-shaped subject.
        var guid = Guid.NewGuid().ToString();
        var token = CustomMachineToken(d =>
        {
            d.Claims["sub"] = guid;
            d.Claims["client_id"] = guid;
        });

        (await PassesAsync(token, MachineParameters)).Should().BeFalse();
        (await PassesAsync(CustomMachineToken(_ => { }), MachineParameters)).Should().BeTrue();
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("application/at+jwt")]
    public async Task MachineProfile_RefusesAnyTypeButAtJwt(string type)
    {
        var token = CustomMachineToken(d => d.TokenType = type);

        (await PassesAsync(token, MachineParameters)).Should().BeFalse();
    }

    [Fact]
    public async Task MachineProfile_RefusesASecondAudience()
    {
        var token = CustomMachineToken(d =>
        {
            d.Audience = null;
            d.Claims["aud"] = new[] { MachineClients.Audience, "shop" };
        });

        (await PassesAsync(token, MachineParameters)).Should().BeFalse();
    }

    [Fact]
    public async Task MachineProfile_RefusesAnotherSigningKey()
    {
        using var stranger = new UserInfoTokens();
        var token = stranger.Service.GenerateMachineAccessToken("shop", MachineClients.Scopes.All);

        (await PassesAsync(token, MachineParameters)).Should().BeFalse();
    }

    [Fact]
    public void TheMachineAudience_IsNeitherCodeShapedNorThePlatformAudience()
    {
        // What keeps the userinfo scheme from accepting a machine token is that its audience can
        // never be an application code.
        Regex.IsMatch(MachineClients.Audience, SharedValidationRules.CodePattern).Should().BeFalse();
        MachineClients.Audience.Should().NotBe(_tokens.Settings.Audience);
    }

    [Fact]
    public async Task TheRevocationValidator_DoesNotKnowMachineTokens()
    {
        // Machine tokens are not revocable one by one (the residual risk the PR states): the
        // revocation endpoint's profiles stay the platform and userinfo ones.
        var validator = new IssuedAccessTokenValidator(_tokens.PlatformParameters, _tokens.UserInfoParameters);

        (await validator.ValidateAsync(MachineToken())).IsError.Should().BeTrue();
    }

    // --- a real pipeline ---

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
                    services.AddAuthorization();
                    services.AddSingleton(_blacklist.Object);
                })
                .Configure(app =>
                {
                    // Program.cs's order: authentication, blacklist, authorization.
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<JwtBlacklistValidationMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/platform", () => "platform").RequireAuthorization();
                        endpoints.MapGet("/userinfo", () => "userinfo").RequireAuthorization(
                            new AuthorizeAttribute { AuthenticationSchemes = AccessTokenValidation.UserInfoScheme });
                        endpoints.MapGet("/machine", (HttpContext context) => context.User.FindFirst("client_id")?.Value)
                            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AccessTokenValidation.MachineScheme });
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
        _tokens.Dispose();
    }

    private async Task<HttpStatusCode> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Pipeline_AMachineToken_OpensOnlyTheMachineScheme()
    {
        var token = MachineToken();

        (await GetAsync("/machine", token)).Should().Be(HttpStatusCode.OK);
        (await GetAsync("/platform", token)).Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync("/userinfo", token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pipeline_PlatformAndApplicationTokens_DoNotOpenTheMachineScheme()
    {
        (await GetAsync("/machine", _tokens.ForPlatform(_user))).Should().Be(HttpStatusCode.Unauthorized);
        (await GetAsync("/machine", _tokens.ForApplication(_user, "openid"))).Should().Be(HttpStatusCode.Unauthorized);

        // And both still open their own schemes, so the 401s above are the machine scheme's.
        (await GetAsync("/platform", _tokens.ForPlatform(_user))).Should().Be(HttpStatusCode.OK);
        (await GetAsync("/userinfo", _tokens.ForApplication(_user, "openid"))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pipeline_AMachineToken_PassesTheBlacklistMiddleware_WithoutAUserOrSessionCheck()
    {
        // No sid, and a sub that is not a GUID: the session and user-revocation checks have
        // nothing to key on, and must let the token through rather than fail it.
        (await GetAsync("/machine", MachineToken())).Should().Be(HttpStatusCode.OK);

        _blacklist.Verify(b => b.IsTokenBlacklisted(It.IsAny<string>()), Times.Once);
        _blacklist.Verify(b => b.IsSessionBlacklisted(It.IsAny<string>()), Times.Never);
        _blacklist.Verify(b => b.AreUserTokensBlacklisted(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
    }
}
