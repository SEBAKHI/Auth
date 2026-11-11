using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.GoogleClientSecret;
using Auth.Application.Features.Secrets.SetConnectionString;
using Auth.Application.Features.Secrets.SetSmtpPassword;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Common.Errors;
using Auth_API.Modules.Administration.Contracts;
using Auth_API.Modules.Administration.Controllers;
using Auth_Localization.Extensions;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// REVIEW 162 F3: a secret sent to the console's Secrets endpoints never reaches a log line. MVC
/// writes "Executing action method … with arguments (…)" at Trace with each argument's
/// <c>ToString()</c>, and a record's default one prints every member: the request record of the
/// SMTP password, Google's client secret and the custom secrets (<see cref="SetSecretRequest"/>),
/// and the three commands that carry a value, print it as <c>[redacted]</c> instead.
/// </summary>
/// <remarks>
/// Deliberate break, verified red: the override on <see cref="SetSecretRequest"/> removed.
/// </remarks>
public sealed class SecretValueLogShapeTests
{
    private const string Secret = "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";

    [Fact]
    public async Task PuttingGoogleClientSecret_AtTrace_LogsTheArgumentsButNeverTheValue()
    {
        var lines = new LineCollector();
        using var host = await StartAsync(lines);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/Secrets/google-client-secret")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { value = Secret }), Encoding.UTF8, "application/json")
        };
        var response = await host.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the request reached the action");
        var all = lines.Lines.ToArray();
        all.Should().Contain(line => line.Contains("Executing action method") && line.Contains("SetSecretRequest"),
            "the floor: MVC's argument line was written, so its content is what is checked");
        all.Should().NotContain(line => line.Contains(Secret), "the secret must never reach a log line");
        await host.StopAsync();
    }

    [Fact]
    public void TheRecordsThatCarryASecret_PrintItRedacted()
    {
        var printed = new[]
        {
            new SetSecretRequest { Value = Secret }.ToString(),
            new SetGoogleClientSecretCommand(Secret, Guid.NewGuid()).ToString(),
            new SetSmtpPasswordCommand(Secret, Guid.NewGuid()).ToString(),
            new SetConnectionStringCommand(Secret, ForceSave: true, Guid.NewGuid()).ToString(),
        };

        printed.Should().OnlyContain(text => !text.Contains(Secret) && text.Contains("[redacted]"));
    }

    private static async Task<IHost> StartAsync(LineCollector lines)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<SetGoogleClientSecretCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        return await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthLocalization();
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(lines));
                    services.AddSingleton(sender.Object);
                    services.Configure<SecretManagementSettings>(settings => settings.EnableAdminApi = true);
                    services.AddAuthentication(SignedIn.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.SchemeName, _ => { });
                    // The permission policy the action names, satisfied by any signed-in caller here:
                    // what is under test is the log, not the authorization.
                    services.AddAuthorization(options => options.AddPolicy(
                        $"{Auth_API.Authorization.RequirePermissionAttribute.PolicyPrefix}secrets.manage",
                        policy => policy.RequireAuthenticatedUser()));
                    services.AddControllers()
                        .AddJsonOptions(options => ApiJsonOptions.Apply(options.JsonSerializerOptions))
                        .AddApiErrorContract()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new SecretsControllerOnly());
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
    }

    /// <summary>Every request is a signed-in administrator.</summary>
    private sealed class SignedIn(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>Every rendered line, with its exception's text.</summary>
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

    private sealed class SecretsControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(SecretsController).GetTypeInfo());
        }
    }
}
