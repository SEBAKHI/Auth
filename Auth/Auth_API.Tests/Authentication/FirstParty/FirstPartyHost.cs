using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;
using Auth_API.Common.FirstParty;
using Auth_API.Modules.Authentication.Controllers;
using Auth_Localization.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// The REAL <see cref="AuthController"/> and <see cref="TwoFactorController"/> — their
/// attributes, filters and credential reading — on a test server, with the
/// mediator mocked. What these tests assert is what a browser would receive:
/// status, <c>Set-Cookie</c> headers and body.
/// </summary>
public sealed class FirstPartyHost : IAsyncDisposable
{
    public const string ConsoleApp = "https://console.example.com";
    public const string AccountsApp = "https://accounts.example.com";
    public const string Apex = "https://example.com";
    public const string Sibling = "https://evil.example.com";

    private readonly IHost _host;

    private FirstPartyHost(IHost host, Mock<ISender> sender, WarningCollector warnings)
    {
        _host = host;
        Sender = sender;
        Warnings = warnings.Messages;
        Client = host.GetTestClient();
    }

    public Mock<ISender> Sender { get; }

    public HttpClient Client { get; }

    /// <summary>Every Warning-or-higher message the host logged, rendered.</summary>
    public IReadOnlyCollection<string> Warnings { get; }

    /// <param name="cookieEnabled">
    /// The refresh-cookie switch; null leaves the settings class's shipped default.
    /// </param>
    public static async Task<FirstPartyHost> StartAsync(
        string[] firstPartyOrigins,
        bool? cookieEnabled,
        string[]? corsOrigins = null)
    {
        var sender = new Mock<ISender>();
        var warnings = new WarningCollector();
        var cors = corsOrigins ?? [ConsoleApp, AccountsApp, Apex];

        var host = await new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                cors.Select((origin, index) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", origin))))
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.AddSingleton(sender.Object);
                    services.AddLogging(logging => logging.AddProvider(warnings));
                    services.Configure<IdentityProviderSettings>(settings =>
                    {
                        settings.FirstPartySpaOrigins = firstPartyOrigins;
                        if (cookieEnabled is { } enabled)
                        {
                            settings.SpaRefreshCookieEnabled = enabled;
                        }
                    });
                    services.PostConfigure<IdentityProviderSettings>(SettingsArrayNormalizer.Apply);

                    // The registrations Program.cs makes.
                    services.AddScoped<IFirstPartyOriginResolver>(provider =>
                        FirstPartyOriginResolver.From(provider.GetRequiredService<IOptionsSnapshot<IdentityProviderSettings>>()));
                    services.AddSingleton<IRefreshCredentialSource, BodyRefreshCredentialSource>();
                    services.AddSingleton<IRefreshCredentialSource, FirstPartyCookieCredentialSource>();
                    services.AddScoped<RefreshCredentialReader>();

                    services.AddAuthentication(TestUserHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestUserHandler>(TestUserHandler.SchemeName, null);
                    services.AddAuthorization();

                    services.AddControllers()
                        .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new AuthControllersOnly());
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

        return new FirstPartyHost(host, sender, warnings);
    }

    /// <summary>A POST as a browser page on <paramref name="origin"/> would send it.</summary>
    public Task<HttpResponseMessage> PostAsync(
        string path,
        string json = "{}",
        string? origin = null,
        IEnumerable<string>? cookies = null,
        bool signedIn = false,
        Guid? sessionId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (cookies is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies));
        }

        if (signedIn)
        {
            request.Headers.TryAddWithoutValidation(TestUserHandler.UserHeader, Guid.NewGuid().ToString());
        }

        if (sessionId is { } session)
        {
            request.Headers.TryAddWithoutValidation(TestUserHandler.SessionHeader, session.ToString());
        }

        return Client.SendAsync(request);
    }

    public static string RefreshCookieOf(string origin) => FirstPartyOriginResolver.CookieNameFor(origin);

    public static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

    public static TokenResponse Tokens(string refreshToken = "real-refresh-token", int refreshExpiresIn = 604800) => new()
    {
        AccessToken = "access-token",
        RefreshToken = refreshToken,
        ExpiresIn = 900,
        RefreshExpiresIn = refreshExpiresIn
    };

    public static LoginResponse SignIn(string refreshToken = "real-refresh-token") => new()
    {
        Token = Tokens(refreshToken),
        IdpSessionToken = "idp-session-token"
    };

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>Keeps every Warning-or-higher log message, so a test can assert on a counter event.</summary>
    private sealed class WarningCollector : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Collector(Messages);

        public void Dispose()
        {
        }

        private sealed class Collector(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) messages.Enqueue(formatter(state, exception));
            }
        }
    }

    private sealed class AuthControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(AuthController).GetTypeInfo());
            feature.Controllers.Add(typeof(TwoFactorController).GetTypeInfo());
        }
    }

    /// <summary>
    /// Signs a request in as the user its header names, with a "sub" claim — and a
    /// "sid" claim when a session header is sent, as an access token carries one.
    /// </summary>
    private sealed class TestUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";
        public const string SessionHeader = "X-Test-Session";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new Claim("sub", user.ToString())];
            if (Request.Headers.TryGetValue(SessionHeader, out var session))
            {
                claims.Add(new Claim("sid", session.ToString()));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
