using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Common.Errors;
using Auth_API.Tests.Helpers;
using Auth_Localization.Extensions;
using ErrorOr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// The API host's MVC wiring (<see cref="ApiErrorContractExtensions.AddApiErrorContract"/>) on a
/// real host with a probe controller, versioned the way Program.cs versions the API: handler
/// results, MVC's own 400 and 415, client-error results, a translated SQL exception, and an
/// unsupported API version all answer with the contract of ADR 0001.
/// </summary>
public sealed class ApiErrorContractTests : IAsyncLifetime
{
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
                    services.AddControllers()
                        .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new ProbeControllerOnly());
                        });
                    services.AddApiVersioning(options =>
                    {
                        options.DefaultApiVersion = new ApiVersion(1, 0);
                        options.AssumeDefaultVersionWhenUnspecified = true;
                        options.ReportApiVersions = true;
                        options.ApiVersionReader = ApiVersionReader.Combine(
                            new UrlSegmentApiVersionReader(),
                            new HeaderApiVersionReader("X-Api-Version"),
                            new QueryStringApiVersionReader("api-version"));
                    }).AddMvc();
                })
                .Configure(app =>
                {
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
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

    [Fact]
    public async Task HandlerResult_WithSeveralValidationFailures_Returns400WithErrorsAndPointers()
    {
        var problem = await PostAsync("/api/v1/probe/failures", """{ "newPassword": "", "count": 1 }""");

        problem.AssertContract(HttpStatusCode.BadRequest, "Password.NewRequired");
        Assert.Equal(
            """[{"code":"Password.NewRequired","pointer":"#/newPassword"},{"code":"Paging.PageSizeOutOfRange"}]""",
            problem.Body.GetProperty("errors").GetRawText());
        Assert.Equal("New password is required.", problem.OptionalString("detail"));
        Assert.Equal("/api/v1/probe/failures", problem.OptionalString("instance"));
    }

    [Fact]
    public async Task HandlerResult_WithConflict_Returns409WithAlreadyRevoked()
    {
        var problem = await GetAsync("/api/v1/probe/conflict");

        problem.AssertContract(HttpStatusCode.Conflict, ApiKeyErrors.AlreadyRevoked.Code);
        Assert.False(problem.Body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Body_WithMalformedJson_Returns400WithBadRequest()
    {
        var problem = await PostAsync("/api/v1/probe/echo", """{ "newPassword": """);

        problem.AssertContract(HttpStatusCode.BadRequest, TransportErrorCodes.BadRequest);
        Assert.False(problem.Body.TryGetProperty("errors", out _));
        Assert.DoesNotContain("newPassword", problem.Raw);
    }

    [Fact]
    public async Task Body_WithoutANonNullableMember_ReachesTheAction()
    {
        // Requiredness is the validator's, with its catalog code: MVC adds no implicit [Required].
        using var response = await _client.PostAsync("/api/v1/probe/echo", Json("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Body_WithTextContent_Returns415WithUnsupportedMediaType()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/probe/echo")
        {
            Content = new StringContent("newPassword=x", Encoding.UTF8, "text/plain"),
        };

        var problem = await ProblemResponse.ReadAsync(_client, request);

        problem.AssertContract(HttpStatusCode.UnsupportedMediaType, TransportErrorCodes.UnsupportedMediaType);
    }

    [Fact]
    public async Task ClientErrorResult_WithoutBody_Returns404WithNotFound()
    {
        var problem = await GetAsync("/api/v1/probe/missing");

        problem.AssertContract(HttpStatusCode.NotFound, TransportErrorCodes.NotFound);
    }

    [Fact]
    public async Task Action_WithForeignKeyViolation_Returns409WithReferenceConflict()
    {
        var problem = await GetAsync("/api/v1/probe/delete-referenced");

        problem.AssertContract(HttpStatusCode.Conflict, PersistenceErrors.ReferenceConflict.Code);
    }

    [Fact]
    public async Task Route_WithUnsupportedApiVersionInThePath_Returns404WithNotFound()
    {
        var problem = await GetAsync("/api/v9/probe/conflict");

        problem.AssertContract(HttpStatusCode.NotFound, TransportErrorCodes.NotFound);
    }

    [Fact]
    public async Task Route_WithConflictingApiVersions_Returns400WithBadRequest()
    {
        var problem = await GetAsync("/api/v1/probe/conflict?api-version=9");

        // Asp.Versioning writes this problem itself, with its own type, title and code
        // (AmbiguousApiVersion). The type and title stay; the code is replaced (ADR 0001, F1).
        problem.AssertContract(
            HttpStatusCode.BadRequest,
            TransportErrorCodes.BadRequest,
            title: "Ambiguous API version",
            type: "https://docs.api-versioning.org/problems#ambiguous");
        Assert.DoesNotContain("AmbiguousApiVersion", problem.Raw);
    }

    private Task<ProblemResponse> GetAsync(string path) =>
        ProblemResponse.ReadAsync(_client, new HttpRequestMessage(HttpMethod.Get, path));

    private Task<ProblemResponse> PostAsync(string path, string json) =>
        ProblemResponse.ReadAsync(_client, new HttpRequestMessage(HttpMethod.Post, path) { Content = Json(json) });

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public sealed record ProbeRequest(string NewPassword, int Count);

    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/probe")]
    public sealed class ProbeController : ApiController
    {
        [HttpPost("echo")]
        public IActionResult Echo(ProbeRequest request) => Ok(request);

        [HttpPost("failures")]
        public IActionResult Failures(ProbeRequest request) => Problem(
        [
            Error.Validation(
                PasswordErrors.NewRequired.Code,
                "developer text",
                new Dictionary<string, object> { [ErrorMetadataKeys.Property] = nameof(request.NewPassword) }),
            Error.Validation(
                PagingErrors.PageSizeOutOfRange.Code,
                "developer text",
                new Dictionary<string, object> { [ErrorMetadataKeys.Property] = "PageSize" }),
        ]);

        [HttpGet("conflict")]
        public IActionResult Conflicting() => Problem([ApiKeyErrors.AlreadyRevoked]);

        [HttpGet("missing")]
        public IActionResult Missing() => NotFound();

        [HttpGet("delete-referenced")]
        public IActionResult DeleteReferenced() => throw SqlExceptions.WithNumber(547, ProblemResponse.SecretExceptionText);
    }

    private sealed class ProbeControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(ProbeController).GetTypeInfo());
        }
    }
}
