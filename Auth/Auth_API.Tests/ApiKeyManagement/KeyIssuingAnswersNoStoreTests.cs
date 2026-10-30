using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.ApiKeys.CreateApiKey;
using Auth.Application.Features.ApiKeys.RotateApiKey;
using Auth.Application.Features.WebhookKeys.CreateWebhookKey;
using Auth.Application.Features.WebhookKeys.RotateWebhookKey;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Authorization;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Modules.ApiKeyManagement.Controllers;
using Auth_API.Modules.WebhookKeyManagement.Controllers;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Auth_Localization.Extensions;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth_API.Tests.ApiKeyManagement;

/// <summary>
/// The four answers that carry a new plain key, API key and webhook key, created and rotated, are
/// <c>Cache-Control: no-store</c> on the wire (OI-128 PR B, T6; OI-129): no cache on the way back
/// may keep the key. The real controllers behind the real bearer scheme and permission policy; the
/// mediator answers with a canned success, since what is asserted is the header, not the handler.
/// </summary>
public sealed class KeyIssuingAnswersNoStoreTests : IAsyncLifetime
{
    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<ISender> _sender = new();
    private readonly User _admin = TestHelpers.CreateUser();
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateApiKeyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<CreateApiKeyResponse>)new CreateApiKeyResponse { Id = Guid.NewGuid(), ApiKey = "ak_plain", KeyPrefix = "ak_prod_" });
        _sender.Setup(s => s.Send(It.IsAny<RotateApiKeyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<RotateApiKeyResponse>)new RotateApiKeyResponse
            {
                NewApiKey = "ak_plain", NewKeyPrefix = "ak_prod_", Message = "Rotated.", MessageCode = "ApiKey.Rotated",
                OldKeyExpiresAt = DateTime.UtcNow.AddHours(1),
            });
        _sender.Setup(s => s.Send(It.IsAny<CreateWebhookKeyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<CreateWebhookKeyResponse>)new CreateWebhookKeyResponse { Id = Guid.NewGuid(), WebhookKey = "wk_plain", KeyPrefix = "wk_prod_" });
        _sender.Setup(s => s.Send(It.IsAny<RotateWebhookKeyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<RotateWebhookKeyResponse>)new RotateWebhookKeyResponse { NewWebhookKey = "wk_plain", NewKeyPrefix = "wk_prod_" });

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.AddLogging();
                    services.Configure<IdentityProviderSettings>(_ => { });
                    services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
                    services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
                    services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
                    services.AddSingleton<IAuthorizationMiddlewareResultHandler, MfaForbiddenResultHandler>();
                    services.AddAuthorization();
                    services.AddSingleton(_sender.Object);
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
                            parts.FeatureProviders.Add(new TheseControllersOnly());
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

    public static TheoryData<string, string, HttpStatusCode> Answers => new()
    {
        { "/api/v1/apikeys", """{"applicationId":"33333333-3333-3333-3333-333333333333","name":"Key","permissionIds":["10000000-0000-0000-0000-000000000005"]}""", HttpStatusCode.Created },
        { "/api/v1/apikeys/20000000-0000-0000-0000-000000000001/rotate", "{}", HttpStatusCode.OK },
        { "/api/v1/webhookkeys", """{"applicationId":"33333333-3333-3333-3333-333333333333","name":"Hook","targetUrl":"https://hooks.example.com/in"}""", HttpStatusCode.Created },
        { "/api/v1/webhookkeys/20000000-0000-0000-0000-000000000002/rotate", "{}", HttpStatusCode.OK },
    };

    [Fact]
    public void Answers_IsNotEmpty() => Answers.Count<object[]>().Should().Be(4);

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task AnAnswerHoldingANewPlainKey_IsNoStore(string path, string body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(expected);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadFromJsonAsync<JsonElement>()).ValueKind.Should().Be(JsonValueKind.Object);
    }

    private string AdminToken() =>
        _tokens.Service.GenerateAccessToken(
            _admin,
            [
                PermissionCodes.ApiKeys.Create, PermissionCodes.ApiKeys.Rotate,
                PermissionCodes.WebhookKeys.Create, PermissionCodes.WebhookKeys.Rotate,
            ],
            ["Admin"],
            AccessTokenAuthentication.Unrecorded,
            sessionId: Guid.NewGuid());

    private sealed class TheseControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(ApiKeysController).GetTypeInfo());
            feature.Controllers.Add(typeof(WebhookKeysController).GetTypeInfo());
        }
    }
}
