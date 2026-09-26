using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Auth.Shared.Http.ErrorContract;
using Auth_Localization.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// The shared error pipeline (ADR 0001) on a real ASP.NET Core host: every error path answers
/// with application/problem+json, a published <c>code</c>, <c>traceId</c>, the framework's
/// <c>type</c> and <c>title</c>, the code's sentence as <c>detail</c>, and no exception data.
/// </summary>
public sealed class ErrorPipelineTests : IAsyncLifetime
{
    private const string ExceptionText = ProblemResponse.SecretExceptionText;

    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ErrorContract:Outage:RetryAfterSeconds"] = "17" }))
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.AddErrorContract();
                    services.AddSingleton<IExceptionProblemTranslator, ReferenceConflictTranslator>();
                    services.AddAuthentication(TestAuthentication.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.SchemeName, null);
                    services.AddAuthorization();
                })
                .Configure(app =>
                {
                    app.UseAuthLocalization();
                    app.UseErrorContract();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(MapEndpoints);
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

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ok", () => Results.Ok());
        endpoints.MapGet("/secure", () => Results.Ok()).RequireAuthorization();
        endpoints.MapGet("/admin", () => Results.Ok()).RequireAuthorization(policy => policy.RequireRole("admin"));
        endpoints.MapPost("/json", (Payload payload) => Results.Ok(payload));
        endpoints.MapGet("/status/{status:int}", (int status) => Results.StatusCode(status));
        endpoints.MapGet("/revoked", (HttpContext http) =>
        {
            http.Items[ProblemItems.Code] = ChallengeReasonCodes.TokenRevoked;
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        });
        endpoints.MapGet("/sized", (HttpContext http) =>
        {
            http.Items[ProblemItems.Code] = "Image.FileTooLarge";
            http.Items[ProblemItems.Args] = new object[] { 1024L };
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        });
        endpoints.MapGet("/foreign-code", () => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            detail: "The HTTP resource does not support the API version '9'.",
            extensions: new Dictionary<string, object?> { ["code"] = "UnsupportedApiVersion" }));
        endpoints.MapGet("/validation-problem", () => TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["name"] = ["'Name' must not be empty."] }));
        endpoints.MapGet("/throw/http", () => Throw(new HttpRequestException(ExceptionText)));
        endpoints.MapGet("/throw/timeout", () => Throw(new TaskCanceledException(ExceptionText, new TimeoutException())));
        endpoints.MapGet("/throw/invalid-operation", () => Throw(new InvalidOperationException(ExceptionText)));
        endpoints.MapGet("/throw/key-not-found", () => Throw(new KeyNotFoundException(ExceptionText)));
        endpoints.MapGet("/throw/argument", () => Throw(new ArgumentException(ExceptionText)));
        endpoints.MapGet("/throw/unauthorized-access", () => Throw(new UnauthorizedAccessException(ExceptionText)));
        endpoints.MapGet("/throw/reference", () => Throw(new ReferenceException(blocked: true)));
        endpoints.MapGet("/throw/unrecognized-reference", () => Throw(new ReferenceException(blocked: false)));
        endpoints.MapGet("/throw/after-recording", (HttpContext http) =>
        {
            http.Items[ProblemItems.Code] = "User.NotFound";
            return Throw(new InvalidOperationException(ExceptionText));
        });
    }

    private static IResult Throw(Exception exception) => throw exception;

    [Fact]
    public async Task UnknownRoute_WithoutBody_Returns404WithNotFound()
    {
        var problem = await SendAsync(HttpMethod.Get, "/nowhere");

        problem.AssertContract(HttpStatusCode.NotFound, TransportErrorCodes.NotFound);
        Assert.Equal("/nowhere", problem.Body.GetProperty("instance").GetString());
        Assert.Equal("The requested resource was not found.", problem.Body.GetProperty("detail").GetString());
        Assert.Equal("en", Assert.Single(problem.Response.Content.Headers.ContentLanguage));
    }

    [Fact]
    public async Task KnownRoute_WithWrongMethod_Returns405WithMethodNotAllowed()
    {
        var problem = await SendAsync(HttpMethod.Delete, "/ok");

        problem.AssertContract(HttpStatusCode.MethodNotAllowed, TransportErrorCodes.MethodNotAllowed);
    }

    [Fact]
    public async Task SecureEndpoint_WithoutCredentials_Returns401WithUnauthenticated()
    {
        var problem = await SendAsync(HttpMethod.Get, "/secure");

        problem.AssertContract(HttpStatusCode.Unauthorized, TransportErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task AdminEndpoint_WithoutTheRole_Returns403WithForbidden()
    {
        var problem = await SendAsync(HttpMethod.Get, "/admin", authenticated: true);

        problem.AssertContract(HttpStatusCode.Forbidden, TransportErrorCodes.Forbidden);
    }

    [Fact]
    public async Task JsonEndpoint_WithTextBody_Returns415WithUnsupportedMediaType()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/json")
        {
            Content = new StringContent("name=x", System.Text.Encoding.UTF8, "text/plain"),
        };

        var problem = await SendAsync(request);

        problem.AssertContract(HttpStatusCode.UnsupportedMediaType, TransportErrorCodes.UnsupportedMediaType);
    }

    [Fact]
    public async Task JsonEndpoint_WithMalformedBody_Returns400WithBadRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/json")
        {
            Content = new StringContent("{ \"name\": ", System.Text.Encoding.UTF8, "application/json"),
        };

        var problem = await SendAsync(request);

        problem.AssertContract(HttpStatusCode.BadRequest, TransportErrorCodes.BadRequest);
        Assert.False(problem.Body.TryGetProperty("errors", out _));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, TransportErrorCodes.BadRequest)]
    [InlineData(StatusCodes.Status409Conflict, TransportErrorCodes.BadRequest)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, TransportErrorCodes.ContentTooLarge)]
    [InlineData(StatusCodes.Status429TooManyRequests, TransportErrorCodes.RateLimited)]
    [InlineData(StatusCodes.Status500InternalServerError, TransportErrorCodes.Unexpected)]
    [InlineData(StatusCodes.Status501NotImplemented, TransportErrorCodes.Unexpected)]
    [InlineData(StatusCodes.Status502BadGateway, TransportErrorCodes.Unavailable)]
    [InlineData(StatusCodes.Status504GatewayTimeout, TransportErrorCodes.Unavailable)]
    public async Task EmptyStatus_WithoutRecordedCode_ReturnsProblemWithTransportCode(int status, string code)
    {
        var problem = await SendAsync(HttpMethod.Get, $"/status/{status}");

        problem.AssertContract((HttpStatusCode)status, code);
    }

    [Fact]
    public async Task EmptyServiceUnavailable_WithoutRetryAfter_Returns503WithConfiguredRetryAfter()
    {
        var problem = await SendAsync(HttpMethod.Get, "/status/503");

        problem.AssertContract(HttpStatusCode.ServiceUnavailable, TransportErrorCodes.Unavailable);
        Assert.Equal(TimeSpan.FromSeconds(17), problem.Response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task EmptyStatus_WithRecordedReason_Returns401WithTokenRevoked()
    {
        var problem = await SendAsync(HttpMethod.Get, "/revoked");

        problem.AssertContract(HttpStatusCode.Unauthorized, ChallengeReasonCodes.TokenRevoked);
        Assert.Equal(
            "Your sign-in is no longer valid. Please sign in again.",
            problem.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task EmptyStatus_WithRecordedArguments_Returns413WithTheirSentence()
    {
        var problem = await SendAsync(HttpMethod.Get, "/sized");

        problem.AssertContract(HttpStatusCode.RequestEntityTooLarge, "Image.FileTooLarge");
        Assert.Equal(
            "The file exceeds the maximum size of 1024 bytes.",
            problem.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnknownRoute_InArabic_Returns404WithArabicDetail()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nowhere");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("ar"));

        var problem = await SendAsync(request);

        problem.AssertContract(HttpStatusCode.NotFound, TransportErrorCodes.NotFound);
        Assert.Equal("لم يتم العثور على المورد المطلوب.", problem.Body.GetProperty("detail").GetString());
        Assert.Equal("ar", Assert.Single(problem.Response.Content.Headers.ContentLanguage));
    }

    [Fact]
    public async Task ProblemResult_WithALibraryCode_Returns400WithBadRequest()
    {
        var problem = await SendAsync(HttpMethod.Get, "/foreign-code");

        problem.AssertContract(HttpStatusCode.BadRequest, TransportErrorCodes.BadRequest);
        Assert.Equal(
            "The request is malformed and could not be processed.",
            problem.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ValidationProblemResult_WithFieldMessages_Returns400WithoutErrors()
    {
        var problem = await SendAsync(HttpMethod.Get, "/validation-problem");

        // The framework titles a validation problem itself; the customization never touches title.
        problem.AssertContract(
            HttpStatusCode.BadRequest, TransportErrorCodes.BadRequest, title: "One or more validation errors occurred.");
        Assert.False(problem.Body.TryGetProperty("errors", out _));
        Assert.DoesNotContain("must not be empty", problem.Raw);
    }

    [Theory]
    [InlineData("/throw/http")]
    [InlineData("/throw/timeout")]
    public async Task Endpoint_WithDependencyOutage_Returns503WithUnavailable(string path)
    {
        var problem = await SendAsync(HttpMethod.Get, path);

        problem.AssertContract(HttpStatusCode.ServiceUnavailable, TransportErrorCodes.Unavailable);
        Assert.Equal(TimeSpan.FromSeconds(17), problem.Response.Headers.RetryAfter?.Delta);
    }

    [Theory]
    [InlineData("/throw/invalid-operation")]
    [InlineData("/throw/key-not-found")]
    [InlineData("/throw/argument")]
    [InlineData("/throw/unauthorized-access")] // a filesystem ACL denial, not an HTTP 401
    [InlineData("/throw/unrecognized-reference")]
    [InlineData("/throw/after-recording")]
    public async Task Endpoint_WithProgrammingError_Returns500WithUnexpected(string path)
    {
        var problem = await SendAsync(HttpMethod.Get, path);

        problem.AssertContract(HttpStatusCode.InternalServerError, TransportErrorCodes.Unexpected);
        Assert.Equal("An unexpected error occurred.", problem.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Endpoint_WithTranslatedException_Returns409WithReferenceConflict()
    {
        var problem = await SendAsync(HttpMethod.Get, "/throw/reference");

        problem.AssertContract(HttpStatusCode.Conflict, "Persistence.ReferenceConflict");
    }

    [Fact]
    public async Task UnknownRoute_WithHtmlOnlyAccept_ReturnsNoProblemBody()
    {
        // The framework writes a problem only for an Accept that admits JSON (ADR 0001):
        // a client that sends another media type gets no code.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nowhere");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void TransportCodes_OneForEveryPublishedTransportStatus()
    {
        var published = PublishedErrorCodes.All.Values.Where(code => code.Source == "transport").ToList();

        Assert.Equal(
            published.Select(code => code.Code).Order(StringComparer.Ordinal),
            TransportErrorCodes.All.Order(StringComparer.Ordinal));
        Assert.All(published, code => Assert.Equal(code.Code, TransportErrorCodes.For(code.Status)));
    }

    [Fact]
    public void ChallengeReasons_AreThePublishedChallengeCodes()
    {
        var published = PublishedErrorCodes.All.Values.Where(code => code.Source == "challenge").ToList();

        Assert.Equal(
            published.Select(code => code.Code).Order(StringComparer.Ordinal),
            ChallengeReasonCodes.All.Order(StringComparer.Ordinal));
        Assert.All(published, code => Assert.Equal(StatusCodes.Status401Unauthorized, code.Status));
    }

    private Task<ProblemResponse> SendAsync(HttpMethod method, string path, bool authenticated = false)
    {
        var request = new HttpRequestMessage(method, path);
        if (authenticated)
        {
            request.Headers.Add(TestAuthentication.UserHeader, "user-1");
        }

        return SendAsync(request);
    }

    private Task<ProblemResponse> SendAsync(HttpRequestMessage request) => ProblemResponse.ReadAsync(_client, request);

    private sealed record Payload(string Name);

    /// <summary>A stand-in for a driver exception that a host translates, like SQL error 547.</summary>
    private sealed class ReferenceException(bool blocked) : Exception(ExceptionText)
    {
        public bool Blocked { get; } = blocked;
    }

    private sealed class ReferenceConflictTranslator : IExceptionProblemTranslator
    {
        public Type ExceptionType => typeof(ReferenceException);

        public ExceptionProblem? Translate(Exception exception) =>
            ((ReferenceException)exception).Blocked
                ? new ExceptionProblem(StatusCodes.Status409Conflict, "Persistence.ReferenceConflict")
                : null;
    }

    /// <summary>Authenticates a request that carries <see cref="UserHeader"/>, and no other.</summary>
    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
