using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.SetPlatformFont;
using Auth.Domain.Enums;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;
using Auth_API.Modules.Administration.Controllers;
using Auth_API.Tests.ErrorContract;
using Auth_Localization.Extensions;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Branding;

/// <summary>
/// The platform's fonts at the HTTP edge (UI batch P4): the real
/// <see cref="PlatformSettingsController"/> on a test host with the API's error contract, and a
/// sender that records the command. Proves what a handler test cannot: the role binds from the
/// path as the console sends it, any other role is not a route, and the file arrives as the
/// multipart body carried it.
/// </summary>
/// <remarks>
/// Deliberate breaks, each turning a case red: drop the route's role constraint (an unknown role
/// reaches the handler); read the upload from another form field; send a removal with a file.
/// </remarks>
public sealed class PlatformFontEndpointTests : IAsyncLifetime
{
    private readonly Mock<ISender> _sender = new();
    private readonly List<(FontRole Role, long? Length, string? FileName, byte[] Bytes)> _sent = [];
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _sender
            .Setup(s => s.Send(It.IsAny<SetPlatformFontCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (IRequest<ErrorOr<PlatformSettingsDto>> request, CancellationToken token) =>
            {
                var command = (SetPlatformFontCommand)request;
                var bytes = Array.Empty<byte>();
                if (command.Upload is { } upload)
                {
                    using var copy = new MemoryStream();
                    await upload.Content.CopyToAsync(copy, token);
                    bytes = copy.ToArray();
                }

                _sent.Add((command.Role, command.Upload?.LengthBytes, command.Upload?.FileName, bytes));
                return (ErrorOr<PlatformSettingsDto>)new PlatformSettingsDto { PlatformName = "Acme" };
            });

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.Configure<SecretManagementSettings>(settings => settings.EnableAdminApi = false);
                    services.AddSingleton(_sender.Object);
                    services.AddAuthentication(SignedIn.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddSingleton<IAuthorizationPolicyProvider, AnySignedInCaller>();
                    services.AddRateLimiter(options => options.AddConcurrencyLimiter(
                        "image-upload", limiter => limiter.PermitLimit = 10));
                    services.AddControllers()
                        .AddJsonOptions(options =>
                        {
                            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                        })
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new PlatformSettingsControllerOnly());
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
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseRateLimiter();
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
    }

    private static MultipartFormDataContent Form(byte[] bytes, string field = "file", string fileName = "Brand.woff2")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("font/woff2");
        return new MultipartFormDataContent { { file, field, fileName } };
    }

    [Theory]
    [InlineData("heading", FontRole.Heading)]
    [InlineData("body", FontRole.Body)]
    public async Task AFont_ArrivesWithItsRole_ItsBytes_AndItsName(string role, FontRole bound)
    {
        var bytes = PlatformFontsTests.Woff2("Brand");

        using var response = await _client.PutAsync($"/api/v1/admin/platform-settings/fonts/{role}", Form(bytes));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sent = _sent.Should().ContainSingle().Subject;
        sent.Role.Should().Be(bound);
        sent.Length.Should().Be(bytes.Length);
        sent.FileName.Should().Be("Brand.woff2");
        sent.Bytes.Should().Equal(bytes);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("heading..")]
    public async Task AnyOtherRole_IsNotARoute(string role)
    {
        using var response = await _client.PutAsync(
            $"/api/v1/admin/platform-settings/fonts/{role}", Form(PlatformFontsTests.Woff2()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AFormWithoutTheFile_IsRefusedWithThePublishedCode()
    {
        var problem = await ProblemResponse.ReadAsync(_client, new HttpRequestMessage(
            HttpMethod.Put, "/api/v1/admin/platform-settings/fonts/body")
        {
            Content = Form(PlatformFontsTests.Woff2(), field: "font"),
        });

        // The body is the contract's. The media type is not: this controller's
        // [Produces("application/json")] overrides application/problem+json on every error it
        // answers (as on five other controllers), a deviation older than this endpoint.
        problem.Response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.Body.GetProperty("code").GetString().Should().Be("Font.FileRequired");
        PublishedErrorCodes.Contains("Font.FileRequired").Should().BeTrue();
        problem.Body.GetProperty("status").GetInt32().Should().Be(400);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_SendsTheRoleAndNoFile()
    {
        using var response = await _client.DeleteAsync("/api/v1/admin/platform-settings/fonts/heading");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sent = _sent.Should().ContainSingle().Subject;
        sent.Role.Should().Be(FontRole.Heading);
        sent.Length.Should().BeNull();
    }

    #region Host

    private sealed class SignedIn(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "SignedIn";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>Every permission policy is "signed in": this host tests binding, not authorization.</summary>
    private sealed class AnySignedInCaller(IOptions<AuthorizationOptions> options)
        : DefaultAuthorizationPolicyProvider(options)
    {
        public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            Task.FromResult<AuthorizationPolicy?>(
                new AuthorizationPolicyBuilder(SignedIn.SchemeName).RequireAuthenticatedUser().Build());
    }

    private sealed class PlatformSettingsControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(PlatformSettingsController).GetTypeInfo());
        }
    }

    #endregion
}
