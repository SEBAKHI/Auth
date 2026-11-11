using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using Auth.Application.Features.Passkeys.BeginPasskeyRegistration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.CompletePasskeyRegistration;
using Auth.Application.Features.Passkeys.SendPasskeyRegistrationEmailCode;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Passkeys;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Auth_Localization.Extensions;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// OI-280 PR B (T-B6): a token issued to an application never reaches the routes that add a
/// passkey. The gate lets a recent sign-in by code stand for the mailbox proof (D-S20-21), and
/// measures that from the session's creation; the code exchange gives an application's session the
/// methods of an older SSO session, so there that time is not the proof's. The proof that such a
/// token cannot get there is this test, not a reading of the schemes: the REAL
/// <see cref="PasskeysController"/>, with the API's three bearer schemes and the passkeys gate where
/// Program.cs puts them. An application's token is refused 401 at all three registration routes
/// before any command is sent; a console token reaches the controller.
/// </summary>
/// <remarks>
/// Deliberate break, verified red: the controller's <c>[Authorize]</c> made to accept the userinfo
/// scheme beside the platform one.
/// </remarks>
public sealed class PasskeyRegistrationAudienceTests : IAsyncLifetime
{
    private readonly UserInfoTokens _tokens = new();
    private readonly User _user = TestHelpers.CreateUser();
    private readonly Mock<ISender> _sender = new();
    private IHost _host = null!;
    private HttpClient _client = null!;

    public static TheoryData<string> RegistrationRoutes => new()
    {
        "/api/v1/auth/passkeys/registration/options",
        "/api/v1/auth/passkeys/registration/email-code",
        "/api/v1/auth/passkeys/registration/verify",
    };

    [Theory]
    [MemberData(nameof(RegistrationRoutes))]
    public async Task Registration_WithAnApplicationsToken_Returns401_AndSendsNothing(string path)
    {
        using var response = await PostAsync(path, _tokens.ForApplication(_user, "openid"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _sender.Invocations.Should().BeEmpty("an application's token never reaches the controller");
    }

    [Theory]
    [MemberData(nameof(RegistrationRoutes))]
    public async Task Registration_WithAConsoleToken_ReachesTheController(string path)
    {
        // The control: the 401 above belongs to the application's token, not to the route.
        using var response = await PostAsync(path, _tokens.ForPlatform(_user));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the mediator's answer, a fresh sign-in, reached the wire");
        _sender.Invocations.Should().ContainSingle();
    }

    public async Task InitializeAsync()
    {
        _sender.Setup(s => s.Send(It.IsAny<BeginPasskeyRegistrationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<PasskeyRegistrationStarted>)AuthErrors.ReauthenticationRequired);
        _sender.Setup(s => s.Send(It.IsAny<SendPasskeyRegistrationEmailCodeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<TwoFactorEmailCodeResponse>)AuthErrors.ReauthenticationRequired);
        _sender.Setup(s => s.Send(It.IsAny<CompletePasskeyRegistrationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<PasskeyDto>)AuthErrors.ReauthenticationRequired);

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.AddLogging();
                    services.Configure<IdentityProviderSettings>(_ => { });
                    services.AddScoped(_ => PasskeyTestDoubles.Feature(enabled: true));
                    services.AddSingleton(_sender.Object);

                    // The API's three schemes, from the same arguments as Program.cs passes.
                    services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.SigningKeys);
                    services.AddAuthorization();
                    services.AddControllers()
                        .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new PasskeysControllerOnly());
                        });
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                        options.ApiVersionReader = new UrlSegmentApiVersionReader();
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    // Program.cs's order: the error contract, the passkeys gate before
                    // authentication, then authorization.
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseMiddleware<PasskeysAvailabilityMiddleware>();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
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

    private async Task<HttpResponseMessage> PostAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            // What the verify route binds; the other two read no body.
            Content = new StringContent("{\"credential\":{\"id\":\"AQIDBA\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    private sealed class PasskeysControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(PasskeysController).GetTypeInfo());
        }
    }
}
