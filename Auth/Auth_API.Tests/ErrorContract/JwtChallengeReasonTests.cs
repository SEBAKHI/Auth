using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;
using Auth_Localization.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// A real JwtBearer challenge with <see cref="JwtChallengeReasons.Record"/> as its OnChallenge,
/// as Program.cs wires it: an expired token is refused with <c>Http.TokenExpired</c>, any other
/// refusal with <c>Http.Unauthenticated</c>, and WWW-Authenticate is kept (ADR 0001).
/// </summary>
public sealed class JwtChallengeReasonTests : IAsyncLifetime
{
    private static readonly SymmetricSecurityKey SigningKey = new(new byte[32]);

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
                    services.AddAuthLocalization();
                    services.AddErrorContract();
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = false,
                            ValidateAudience = false,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero,
                            IssuerSigningKey = SigningKey,
                        };
                        options.Events = new JwtBearerEvents { OnChallenge = JwtChallengeReasons.Record };
                    });
                    services.AddAuthorization();
                })
                .Configure(app =>
                {
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/secure", () => Results.Ok()).RequireAuthorization());
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

    [Fact]
    public async Task SecureEndpoint_WithExpiredToken_Returns401WithTokenExpired()
    {
        var problem = await SendAsync(Token(expires: DateTime.UtcNow.AddMinutes(-5)));

        problem.AssertContract(HttpStatusCode.Unauthorized, ChallengeReasonCodes.TokenExpired);
        Assert.Equal("Bearer", problem.Response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task SecureEndpoint_WithForgedToken_Returns401WithUnauthenticated()
    {
        var forged = Token(expires: DateTime.UtcNow.AddMinutes(5), key: new SymmetricSecurityKey(Enumerable.Repeat((byte)7, 32).ToArray()));

        var problem = await SendAsync(forged);

        problem.AssertContract(HttpStatusCode.Unauthorized, TransportErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task SecureEndpoint_WithValidToken_ReturnsOk()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(expires: DateTime.UtcNow.AddMinutes(5)));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<ProblemResponse> SendAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ProblemResponse.ReadAsync(_client, request);
    }

    private static string Token(DateTime expires, SecurityKey? key = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore: expires.AddMinutes(-30),
            expires: expires,
            signingCredentials: new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.HmacSha256)));
}
