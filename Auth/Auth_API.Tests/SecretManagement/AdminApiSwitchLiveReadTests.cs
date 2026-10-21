using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Modules.Administration.Filters;
using Auth_Localization.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// AM-S11-1 (owner decision OWN-AMS111): the secret-management admin API switch is read on
/// every request, from the configuration in force, so turning it on or off is a settings
/// edit that takes effect on the next request, and a settings file that fails to parse, or
/// goes missing, closes it instead of leaving it open. A monitor would keep the last "true":
/// a reload that fails to parse drops the file's values before the monitor is told.
/// </summary>
public sealed class AdminApiSwitchLiveReadTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "authsystem-admin-switch", Guid.NewGuid().ToString("N"));

    public AdminApiSwitchLiveReadTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(BasePath, """{ "SecretManagement": { "EnableAdminApi": false } }""");
    }

    private string BasePath => Path.Combine(_directory, "appsettings.json");

    private string ProductionPath => Path.Combine(_directory, "appsettings.Production.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }

    [Fact]
    public async Task TurningItOn_TakesEffectOnTheNextRequest_WithNoRestart()
    {
        WriteProduction(enabled: false);
        var (files, services) = Build();
        (await StatusOfARequestAsync(services)).Should().Be(StatusCodes.Status403Forbidden);

        WriteProduction(enabled: true);
        files.FileChanged();

        (await StatusOfARequestAsync(services)).Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task AReloadOfASettingsFileThatFailedToParse_ClosesIt()
    {
        WriteProduction(enabled: true);
        var (files, services) = Build();
        (await StatusOfARequestAsync(services)).Should().Be(StatusCodes.Status200OK);

        File.WriteAllText(ProductionPath, """{ "SecretManagement": { "EnableAdminApi": true, """);
        var reload = () => files.FileChanged();
        reload.Should().Throw<Exception>("the half-written file does not parse");

        (await StatusOfARequestAsync(services)).Should().Be(StatusCodes.Status403Forbidden,
            "the broken file contributes nothing, and the base file ships the switch off");
    }

    [Fact]
    public async Task ASettingsFileThatGoesMissing_ClosesIt()
    {
        WriteProduction(enabled: true);
        var (files, services) = Build();
        (await StatusOfARequestAsync(services)).Should().Be(StatusCodes.Status200OK);

        File.Delete(ProductionPath);
        files.FileChanged();

        (await StatusOfARequestAsync(services)).Should().Be(StatusCodes.Status403Forbidden);
    }

    private void WriteProduction(bool enabled) =>
        File.WriteAllText(ProductionPath, $$"""{ "SecretManagement": { "EnableAdminApi": {{(enabled ? "true" : "false")}} } }""");

    // The production file is watched as the host watches it (reloadOnChange), through a file
    // provider whose change the test raises, so a reload runs the same path a save on the
    // server runs: the one that drops the file values when they no longer parse.
    private (WatchedFiles Files, ServiceProvider Services) Build()
    {
        var files = new WatchedFiles(_directory);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(BasePath, optional: false, reloadOnChange: false)
            .Add(new JsonConfigurationSource
            {
                FileProvider = files,
                Path = Path.GetFileName(ProductionPath),
                Optional = true,
                ReloadOnChange = true,
                ReloadDelay = 0
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        services.AddAuthLocalization();
        services.AddErrorContract();
        services.Configure<SecretManagementSettings>(configuration.GetSection(SecretManagementSettings.SectionName));

        return (files, services.BuildServiceProvider());
    }

    /// <summary>The settings folder, with a change notification the test raises.</summary>
    private sealed class WatchedFiles(string root) : IFileProvider
    {
        private readonly PhysicalFileProvider _inner = new(root);
        private CancellationTokenSource _changed = new();

        public IFileInfo GetFileInfo(string subpath) => _inner.GetFileInfo(subpath);

        public IDirectoryContents GetDirectoryContents(string subpath) => _inner.GetDirectoryContents(subpath);

        public IChangeToken Watch(string filter) => new CancellationChangeToken(_changed.Token);

        /// <summary>The file changed on disk: the configuration reloads it, now, on this thread.</summary>
        public void FileChanged()
        {
            var changed = _changed;
            _changed = new CancellationTokenSource();
            changed.Cancel();
        }
    }

    // One request: a fresh scope, as ASP.NET Core gives every request, through the filter.
    private static async Task<int> StatusOfARequestAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());
        var reached = false;

        await new RequireAdminApiEnabledAttribute().OnActionExecutionAsync(executing, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()));
        });

        return reached
            ? StatusCodes.Status200OK
            : executing.Result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode!.Value;
    }
}
