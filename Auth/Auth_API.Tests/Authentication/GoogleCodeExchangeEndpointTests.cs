using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Behaviors;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ExchangeGoogleCode;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Infrastructure.Authentication;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Common.Errors;
using Auth_API.Common.FirstParty;
using Auth_API.Modules.Authentication.Controllers;
using Auth_Localization.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S02 PR D: <c>POST /api/v1/auth/external/google/code</c> as a browser meets it. The REAL
/// <see cref="AuthController"/> action, both Origin filters, the real command behind a real
/// mediator with Program.cs's two behaviours, and the real <see cref="GoogleCodeExchanger"/>
/// on a typed client whose handler stands in for Google. What is asserted is the wire, what
/// Google was asked, and every line the host logged.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>[RequireFirstPartyAppOrigin]</c> dropped (a request
/// without an Origin, or with an empty list, reaches Google); the callback address taken from a
/// body member; the answer carrying Google's whole token response; <c>Cache-Control</c> left out;
/// the code written into the handler's log line.
/// </remarks>
public sealed class GoogleCodeExchangeEndpointTests
{
    private const string Path = "/api/v1/auth/external/google/code";
    private const string AccountsOrigin = "https://accounts.example.com";
    private const string ConsoleOrigin = "https://console.example.com";
    private const string ClientSecret = "GOCSPX-endpoint-test-secret-0123";
    private const string Code = "4/0Ab-endpoint-test-one-time-code";
    private const string Verifier = "dBjftJeZ4CVP-mJ92K9FTB9e42p5hj6E2iZlqZzX7pN0aQ";
    private const string IdToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiI0MiJ9.endpoint-test-id-token";
    private const string AccessToken = "ya29.endpoint-test-access-token";
    private const string RefreshToken = "1//endpoint-test-refresh-token";

    private static readonly string GoogleSuccess =
        $$"""{"access_token":"{{AccessToken}}","refresh_token":"{{RefreshToken}}","token_type":"Bearer","id_token":"{{IdToken}}"}""";

    private static string Body(string code = Code, string verifier = Verifier, object? extra = null) =>
        JsonSerializer.Serialize(extra is null
            ? new Dictionary<string, object> { ["code"] = code, ["codeVerifier"] = verifier }
            : new Dictionary<string, object> { ["code"] = code, ["codeVerifier"] = verifier, ["redirectUri"] = extra });

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example.net")]
    [InlineData("https://sub.accounts.example.com")]
    [InlineData("http://accounts.example.com")]
    [InlineData("null")]
    public async Task Exchange_WithoutAListedOrigin_Returns403_AndNeverCallsGoogle(string? origin)
    {
        await using var host = await ExchangeHost.StartAsync();

        var response = await host.PostAsync(Body(), origin);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(AuthErrors.FirstPartyOriginRequired.Code);
        host.GoogleRequests.Should().BeEmpty(
            "the callback address is built from the Origin, so without a listed one there is nothing to send");
    }

    [Fact]
    public async Task Exchange_WhileNoAppIsListed_Returns403_EvenWithAnOrigin()
    {
        await using var host = await ExchangeHost.StartAsync(origins: []);

        var response = await host.PostAsync(Body(), AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "unlike RequireFirstPartyOrigin, an empty list refuses rather than admits everyone");
        (await CodeAsync(response)).Should().Be(AuthErrors.FirstPartyOriginRequired.Code);
        host.GoogleRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AccountsOrigin)]
    [InlineData(ConsoleOrigin)]
    public async Task Exchange_FromAListedApp_SendsGoogleThatAppsCallbackAddress_AndReturnsOnlyTheIdToken(string origin)
    {
        await using var host = await ExchangeHost.StartAsync();

        var response = await host.PostAsync(Body(), origin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be($$"""{"idToken":"{{IdToken}}"}""",
            "the ID token and nothing else of Google's answer");
        (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue("no cache keeps a token");
        response.Headers.Contains("Set-Cookie").Should().BeFalse("nobody is signed in here");

        var form = host.GoogleRequests.Should().ContainSingle().Subject;
        form["redirect_uri"].Should().Be(origin + "/google-callback.html");
        form["code"].Should().Be(Code);
        form["code_verifier"].Should().Be(Verifier);
        form["client_secret"].Should().Be(ClientSecret);
    }

    [Fact]
    public async Task Exchange_IgnoresACallbackAddressInTheBody()
    {
        await using var host = await ExchangeHost.StartAsync();

        var response = await host.PostAsync(Body(extra: "https://evil.example.net/catch"), AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.GoogleRequests.Should().ContainSingle().Which["redirect_uri"]
            .Should().Be(AccountsOrigin + "/google-callback.html", "the address is never read from the request");
    }

    [Theory]
    [InlineData("", Verifier, "ExternalAuth.CodeRequired")]
    [InlineData(Code, "too-short", "ExternalAuth.CodeVerifierInvalid")]
    [InlineData(Code, "", "ExternalAuth.CodeVerifierInvalid")]
    public async Task Exchange_WithAMalformedBody_Returns400WithItsCode_BeforeGoogle(string code, string verifier, string expected)
    {
        await using var host = await ExchangeHost.StartAsync();

        var response = await host.PostAsync(Body(code, verifier), AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(expected);
        host.GoogleRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Exchange_WhenGoogleRefusesTheCode_Returns400CodeExchangeFailed()
    {
        await using var host = await ExchangeHost.StartAsync(
            google: () => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));

        var response = await host.PostAsync(Body(), AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(ExternalAuthErrors.CodeExchangeFailed.Code);
    }

    [Fact]
    public async Task Exchange_WhenGoogleIsDown_Returns503HttpUnavailable()
    {
        await using var host = await ExchangeHost.StartAsync(
            google: () => Json(HttpStatusCode.BadGateway, "<html>bad gateway</html>"));

        var response = await host.PostAsync(Body(), AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await CodeAsync(response)).Should().Be(TransportErrorCodes.For(503));
        response.Headers.RetryAfter.Should().NotBeNull("an outage tells the client when to try again");
    }

    [Fact]
    public async Task Exchange_WithoutTheClientSecret_Returns400ProviderNotConfigured_AndNeverCallsGoogle()
    {
        await using var host = await ExchangeHost.StartAsync(secret: null);

        var response = await host.PostAsync(Body(), AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(ExternalAuthErrors.ProviderNotConfigured("google").Code);
        host.GoogleRequests.Should().BeEmpty();
    }

    /// <summary>
    /// Every line the host wrote, at every level, for a success, a refusal and an outage: none
    /// holds the code, the verifier, the secret or a token.
    /// </summary>
    [Fact]
    public async Task Exchange_LogsNothingSecret_ForASuccessARefusalAndAnOutage()
    {
        var answers = new Queue<Func<HttpResponseMessage>>(
        [
            () => Json(HttpStatusCode.OK, GoogleSuccess),
            () => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""),
            () => Json(HttpStatusCode.InternalServerError, $$"""{"error":"{{RefreshToken}}"}"""),
        ]);
        await using var host = await ExchangeHost.StartAsync(google: () => answers.Dequeue()());

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await host.PostAsync(Body(), AccountsOrigin)).StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.ServiceUnavailable);
        var lines = await host.TakeLinesOfRequestsAsync(3);
        lines.Should().Contain(line => line.Contains("Handling"), "the mediator's lines are captured");
        foreach (var secret in new[] { Code, Verifier, ClientSecret, IdToken, AccessToken, RefreshToken })
        {
            lines.Should().NotContain(line => line.Contains(secret), "{0} must never reach a log line", secret);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The test server: Program.cs's pieces for this route, with Google replaced by a handler.</summary>
    private sealed class ExchangeHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly LineCollector _lines;

        private ExchangeHost(IHost host, ConcurrentQueue<Dictionary<string, string>> googleRequests, LineCollector lines)
        {
            _host = host;
            GoogleRequests = googleRequests;
            _lines = lines;
            Client = host.GetTestClient();
        }

        /// <summary>The form of every request sent to Google's token endpoint, in order.</summary>
        public ConcurrentQueue<Dictionary<string, string>> GoogleRequests { get; }

        private HttpClient Client { get; }

        public static async Task<ExchangeHost> StartAsync(
            string[]? origins = null,
            string? secret = ClientSecret,
            Func<HttpResponseMessage>? google = null)
        {
            var googleRequests = new ConcurrentQueue<Dictionary<string, string>>();
            var lines = new LineCollector();
            var answer = google ?? (() => Json(HttpStatusCode.OK, GoogleSuccess));
            var fakeGoogle = new RecordingHandler(googleRequests, answer);

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthLocalization();
                        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(lines));
                        services.Configure<IdentityProviderSettings>(settings =>
                            settings.FirstPartySpaOrigins = origins ?? [ConsoleOrigin, AccountsOrigin]);
                        services.AddScoped<IFirstPartyOriginResolver>(provider =>
                            FirstPartyOriginResolver.From(provider.GetRequiredService<IOptionsSnapshot<IdentityProviderSettings>>()));
                        services.Configure<ExternalAuthSettings>(settings => settings.Google = new GoogleAuthSettings
                        {
                            Enabled = true,
                            ClientId = "crm-client.apps.googleusercontent.com",
                            ClientSecret = secret,
                        });

                        // Program.cs's registration, with Google's network replaced.
                        services.AddHttpClient<IGoogleCodeExchanger, GoogleCodeExchanger>(client =>
                            {
                                client.Timeout = GoogleCodeExchanger.Timeout;
                                client.MaxResponseContentBufferSize = GoogleCodeExchanger.MaxResponseBytes;
                            })
                            .ConfigurePrimaryHttpMessageHandler(() => fakeGoogle);

                        services.AddMediatR(cfg =>
                        {
                            cfg.RegisterServicesFromAssemblyContaining<ExchangeGoogleCodeCommandHandler>();
                            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
                            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                        });
                        services.AddValidatorsFromAssemblyContaining<ExchangeGoogleCodeCommandHandler>();

                        services.AddAuthentication();
                        services.AddAuthorization();
                        services.AddControllers()
                            .AddJsonOptions(options => ApiJsonOptions.Apply(options.JsonSerializerOptions))
                            .AddApiErrorContract()
                            .ConfigureApplicationPartManager(parts =>
                            {
                                parts.ApplicationParts.Clear();
                                parts.FeatureProviders.Add(new AuthControllerOnly());
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

            return new ExchangeHost(host, googleRequests, lines);
        }

        public async Task<HttpResponseMessage> PostAsync(string json, string? origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (origin is not null)
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            var response = await Client.SendAsync(request);
            await response.Content.LoadIntoBufferAsync();
            return response;
        }

        /// <summary>
        /// Every rendered line once <paramref name="requests"/> «Request finished» lines are in
        /// (the host writes that line after the client holds the answer).
        /// </summary>
        public async Task<List<string>> TakeLinesOfRequestsAsync(int requests)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (_lines.Lines.Count(line => line.StartsWith("Request finished", StringComparison.Ordinal)) < requests
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            return [.. _lines.Lines];
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    /// <summary>Google's token endpoint: records each form and answers as told.</summary>
    private sealed class RecordingHandler(
        ConcurrentQueue<Dictionary<string, string>> requests,
        Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri.Should().Be(new Uri(GoogleCodeExchanger.TokenEndpoint));
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            requests.Enqueue(body.Split('&')
                .Select(pair => pair.Split('='))
                .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1])));
            return answer();
        }
    }

    /// <summary>Every line the host logs, rendered, with the exception's text when there is one.</summary>
    private sealed class LineCollector : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Collector(this);

        public void Dispose()
        {
        }

        private sealed class Collector(LineCollector owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Lines.Enqueue(formatter(state, exception) + (exception is null ? "" : " | " + exception));
            }
        }
    }

    private sealed class AuthControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(AuthController).GetTypeInfo());
        }
    }
}
