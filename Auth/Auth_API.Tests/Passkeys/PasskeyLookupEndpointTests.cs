using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Behaviors;
using Auth.Application.Configuration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.PasskeyLookup;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;
using Auth_API.Common.FirstParty;
using Auth_API.Common.Passkeys;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Helpers;
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

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// OI-280 (T-A1, V2): <c>POST /api/v1/auth/passkeys/lookup</c> as a browser meets it. The REAL
/// <see cref="PasskeysController"/>, the 404 gate where Program.cs puts it, the first-party Origin
/// filter, and the real query behind a real mediator with Program.cs's two behaviours, over a
/// repository that knows one address with a passkey. What is asserted is the wire: status,
/// headers, cookies and body, and every line the host logged.
/// </summary>
/// <remarks>
/// <para>
/// An unknown address and an address without a passkey reach this layer as the same false (the
/// statement decides that, PasskeyStoreSqlTextTests); what this pins is that nothing between the
/// answer and the wire says more: the same status, the same body bytes and the same header names
/// for every answer, no cookie, and no line that names the address or differs with the answer.
/// </para>
/// <para>
/// Deliberate breaks, each verified red: the passkey count answered instead of the boolean;
/// <c>[RequireFirstPartyOrigin]</c> dropped; the address logged raw by the handler.
/// </para>
/// </remarks>
public sealed class PasskeyLookupEndpointTests
{
    private const string Path = "/api/v1/auth/passkeys/lookup";
    private const string Holder = "ada.holder@example.com";
    private const string Plain = "bea.plain@example.com";
    private const string Unknown = "cy.nobody@example.com";

    private static async Task<LookupHost> StartAsync(bool passkeysRun = true) => await LookupHost.StartAsync(passkeysRun);

    private static string Body(string email) => JsonSerializer.Serialize(new { email });

    [Fact]
    public async Task LookUp_WhilePasskeysDoNotRun_Returns404_AndSendsNoQuery()
    {
        await using var host = await StartAsync(passkeysRun: false);

        var response = await host.PostAsync(Body(Holder), PasskeyTestDoubles.AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the feature does not exist while it is off (PK19)");
        host.Lookups.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://evil.example.com")]
    [InlineData("null")]
    public async Task LookUp_FromAnotherOrigin_Returns403WithFirstPartyOriginRequired_BeforeTheQuery(string origin)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(Body(Holder), origin);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await CodeAsync(response)).Should().Be(AuthErrors.FirstPartyOriginRequired.Code);
        host.Lookups.Should().BeEmpty("a page of another origin learns nothing, and costs no read");
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    public async Task LookUp_WithAMalformedAddress_Returns400WithTheAddressCode(string email)
    {
        await using var host = await StartAsync();

        var response = await host.PostAsync(Body(email), PasskeyTestDoubles.AccountsOrigin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().StartWith("Email.", "the address's own codes, as the code sign-in's start answers");
        host.Lookups.Should().BeEmpty();
    }

    [Fact]
    public async Task LookUp_ForAnAddressWithoutAPasskey_AndAnUnknownOne_Returns200WithTheSameBytes_AndNothingElse()
    {
        await using var host = await StartAsync();

        var unknown = await host.PostAsync(Body(Unknown), PasskeyTestDoubles.AccountsOrigin);
        var without = await host.PostAsync(Body(Plain), PasskeyTestDoubles.AccountsOrigin);

        foreach (var response in new[] { unknown, without })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("{\"passkey\":false}", "the boolean, and nothing else");
            response.Headers.Contains("Set-Cookie").Should().BeFalse("no cookie: nothing is started");
            (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue("no browser or proxy keeps an answer about an account");
        }

        (await unknown.Content.ReadAsByteArrayAsync()).Should().Equal(await without.Content.ReadAsByteArrayAsync());
        HeaderNames(unknown).Should().Equal(HeaderNames(without));
    }

    [Fact]
    public async Task LookUp_ForAHolder_Returns200WithTrue_AndTheSameHeadersAndShapeAsFalse()
    {
        await using var host = await StartAsync();

        var holder = await host.PostAsync(Body(Holder), PasskeyTestDoubles.AccountsOrigin);
        var other = await host.PostAsync(Body(Plain), PasskeyTestDoubles.AccountsOrigin);

        holder.StatusCode.Should().Be(HttpStatusCode.OK);
        (await holder.Content.ReadAsStringAsync()).Should().Be("{\"passkey\":true}",
            "no credential id, handle, name, count or masked address goes back to an anonymous caller");
        holder.Headers.Contains("Set-Cookie").Should().BeFalse();
        HeaderNames(holder).Should().Equal(HeaderNames(other), "only the boolean differs between the two answers");
        holder.Content.Headers.ContentLength.Should().Be(other.Content.Headers.ContentLength! - 1, "true and false, nothing more");
    }

    [Fact]
    public async Task LookUp_SignedOut_IsAnswered_AndAskedOnceForTheTypedAddress()
    {
        await using var host = await StartAsync();

        (await host.PostAsync(Body("  Ada.Holder@Example.com"), origin: null)).StatusCode.Should().Be(HttpStatusCode.OK,
            "anonymous, and a caller without an Origin (no browser) is not refused by the Origin check");

        host.Lookups.Should().ContainSingle().Which.Should().Be("  Ada.Holder@Example.com",
            "the repository normalizes the address as the accounts store it");
    }

    [Fact]
    public async Task LookUp_LogsNoAddress_AndTheSameLinesForEveryAnswer()
    {
        await using var host = await StartAsync();
        // The first request builds what the host builds once (its route table, its formatters):
        // those lines are the host's, not an answer's.
        await host.PostAsync(Body("warm-up@example.com"), PasskeyTestDoubles.AccountsOrigin);
        await host.TakeRequestLinesAsync();

        await host.PostAsync(Body(Holder), PasskeyTestDoubles.AccountsOrigin);
        var forTrue = await host.TakeRequestLinesAsync();
        await host.PostAsync(Body(Plain), PasskeyTestDoubles.AccountsOrigin);
        var forFalse = await host.TakeRequestLinesAsync();

        forTrue.Should().NotBeEmpty("the host's lines are captured at every level");
        forTrue.Concat(forFalse).Should().OnlyContain(
            line => !line.Rendered.Contains("ada.holder", StringComparison.OrdinalIgnoreCase)
                && !line.Rendered.Contains("bea.plain", StringComparison.OrdinalIgnoreCase),
            "the address is personal data (PK16 and its address rule)");
        // The pipeline warns of a request slower than 500 ms: a line about the machine's load,
        // written for either answer alike, so it is left out of the comparison.
        static IEnumerable<(LogLevel, string)> Shape(IEnumerable<LoggedLine> lines) =>
            lines.Where(line => !line.Template.StartsWith("Long-running request", StringComparison.Ordinal))
                .Select(line => (line.Level, line.Template));
        Shape(forTrue).Should().Equal(
            Shape(forFalse),
            "a line that only some answers write would be a record of who holds a passkey");
    }

    private static List<string> HeaderNames(HttpResponseMessage response) =>
        response.Headers.Select(header => header.Key)
            .Concat(response.Content.Headers.Where(header => header.Key != "Content-Length").Select(header => header.Key))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The test server: Program.cs's pieces for this route, over a repository the test knows.</summary>
    private sealed class LookupHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly LineCollector _lines;

        private LookupHost(IHost host, ConcurrentQueue<string> lookups, LineCollector lines)
        {
            _host = host;
            Lookups = lookups;
            _lines = lines;
            Client = host.GetTestClient();
        }

        /// <summary>The address each read of the passkeys was asked about, in order.</summary>
        public ConcurrentQueue<string> Lookups { get; }

        private HttpClient Client { get; }

        public static async Task<LookupHost> StartAsync(bool passkeysRun)
        {
            var lookups = new ConcurrentQueue<string>();
            var lines = new LineCollector();
            var passkeys = new Mock<IUserPasskeyRepository>();
            passkeys.Setup(r => r.HasLiveForEmailAsync(It.IsAny<string>(), PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string email, string _, CancellationToken _) =>
                {
                    lookups.Enqueue(email);
                    return email.Trim().ToUpperInvariant() == Holder.ToUpperInvariant();
                });

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthLocalization();
                        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(lines));
                        services.Configure<IdentityProviderSettings>(settings =>
                            settings.FirstPartySpaOrigins = [PasskeyTestDoubles.ConsoleOrigin, PasskeyTestDoubles.AccountsOrigin]);
                        services.AddScoped<IFirstPartyOriginResolver>(provider =>
                            FirstPartyOriginResolver.From(provider.GetRequiredService<IOptionsSnapshot<IdentityProviderSettings>>()));

                        // The gate and the handler ask the same per-request feature (AddScoped in Program.cs).
                        services.AddScoped(_ => PasskeyTestDoubles.Feature(enabled: passkeysRun));
                        services.AddSingleton(passkeys.Object);
                        services.AddMediatR(cfg =>
                        {
                            cfg.RegisterServicesFromAssemblyContaining<PasskeyLookupQueryHandler>();
                            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
                            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                        });
                        services.AddValidatorsFromAssemblyContaining<PasskeyLookupQueryHandler>();

                        services.AddAuthentication();
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

            return new LookupHost(host, lookups, lines);
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
        /// One request's lines. The host logs the last of them, «Request finished», after the
        /// client already holds the answer, so they are taken once that line is in; otherwise it
        /// would land among the next request's lines.
        /// </summary>
        public async Task<List<LoggedLine>> TakeRequestLinesAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!_lines.Lines.Any(line =>
                       line.Category == "Microsoft.AspNetCore.Hosting.Diagnostics"
                       && line.Template.StartsWith("Request finished", StringComparison.Ordinal))
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            return TakeLines();
        }

        /// <summary>The lines logged since the last call, and forgets them.</summary>
        public List<LoggedLine> TakeLines()
        {
            var taken = new List<LoggedLine>();
            while (_lines.Lines.TryDequeue(out var line))
            {
                taken.Add(line);
            }

            return taken;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    internal sealed record LoggedLine(LogLevel Level, string Category, string Template, string Rendered);

    /// <summary>Every line the host logs, at every level, with its template and its text.</summary>
    private sealed class LineCollector : ILoggerProvider
    {
        public ConcurrentQueue<LoggedLine> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Collector(categoryName, Lines);

        public void Dispose()
        {
        }

        private sealed class Collector(string category, ConcurrentQueue<LoggedLine> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // The host's own start and stop lines are not the request's.
                if (category.StartsWith("Microsoft.Hosting", StringComparison.Ordinal)) return;

                var template = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.FirstOrDefault(pair => pair.Key == "{OriginalFormat}").Value?.ToString() ?? string.Empty
                    : string.Empty;
                // Timings are not answers: a template, not its numbers, says what a line is.
                lines.Enqueue(new LoggedLine(logLevel, category, template, formatter(state, exception)));
            }
        }
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
