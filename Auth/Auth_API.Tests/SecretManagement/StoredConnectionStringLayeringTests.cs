using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// Covers what happens once the AuthDb connection string and the SMTP password
/// live in the encrypted secrets file rather than in web.config: that they reach
/// configuration under the keys the process actually reads, that the console
/// reports them as configured, and that a stored connection string can be
/// bypassed when it goes stale.
/// </summary>
/// <remarks>
/// The escape hatch is the reason the rest is safe to do at all. A stored
/// connection string that stops working — host renamed, password rotated at the
/// server, site migrated — leaves the API unable to start, and therefore unable
/// to serve the admin console that would correct it. Without a bypass the only
/// remedy is hand-editing an encrypted file.
/// </remarks>
public class StoredConnectionStringLayeringTests : IDisposable
{
    private const string StoredConnectionString =
        "Server=stored-host;Database=AuthDb;User Id=app;Password=stored-pw;Encrypt=False";
    private const string StoredSmtpPassword = "stored-smtp-password";
    private const string FileConnectionString =
        "Server=fallback-host;Database=AuthDb;Integrated Security=true";

    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(), "authsystem-secret-layering", Guid.NewGuid().ToString("N"));

    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _provider;
    private readonly string? _originalEscapeHatch;

    public StoredConnectionStringLayeringTests()
    {
        Directory.CreateDirectory(_workingDirectory);
        _secretFilePath = Path.Combine(_workingDirectory, "secrets.dpapi");

        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("AuthSystem")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_workingDirectory, "keys")));
        _provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();

        _originalEscapeHatch = Environment.GetEnvironmentVariable(
            DpapiSecretConfigurationProvider.IgnoreConnectionStringVariable);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            DpapiSecretConfigurationProvider.IgnoreConnectionStringVariable, _originalEscapeHatch);

        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }

        GC.SuppressFinalize(this);
    }

    private DpapiSecretService CreateService() =>
        new(
            _provider,
            Options.Create(new SecretManagementSettings { SecretFilePath = _secretFilePath }),
            NullLogger<DpapiSecretService>.Instance);

    /// <summary>Stores both values the way the new admin endpoints do.</summary>
    private async Task<IDpapiSecretService> StoreBothSecretsAsync()
    {
        var service = CreateService();
        await service.GenerateMissingKeysAsync(CancellationToken.None);
        await service.SetSecretAsync("ConnectionStrings.AuthDb", StoredConnectionString, CancellationToken.None);
        await service.SetSecretAsync("SmtpPassword", StoredSmtpPassword, CancellationToken.None);
        return service;
    }

    /// <summary>
    /// Models the real layer order: the file/environment value is added first and
    /// the secrets file last, which is why the stored value wins.
    /// </summary>
    private IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthDb"] = FileConnectionString,
                ["Email:Password"] = "file-smtp-password"
            })
            .AddDpapiSecrets(_provider, _secretFilePath)
            .Build();

    [Fact]
    public async Task StoredSecrets_OverrideTheConfigurationLayerBeneathThem()
    {
        await StoreBothSecretsAsync();

        var configuration = BuildConfiguration();

        configuration.GetConnectionString("AuthDb").Should().Be(StoredConnectionString,
            "the secrets layer is added after environment variables, which is what lets the encrypted "
            + "value replace the plaintext one in web.config");
        configuration["Email:Password"].Should().Be(StoredSmtpPassword);
    }

    /// <summary>
    /// The console badge reads these properties directly rather than the resolved
    /// configuration, which is why a value supplied by an environment variable
    /// shows as "not configured" however well it works.
    /// </summary>
    [Fact]
    public async Task StoredSecrets_AreReportedAsConfiguredByTheStatusEndpoint()
    {
        var service = await StoreBothSecretsAsync();

        var status = await service.GetStatusAsync(CancellationToken.None);

        status.Secrets["ConnectionStrings.AuthDb"].Should().Be(SecretStatus.Configured);
        status.Secrets["SmtpPassword"].Should().Be(SecretStatus.Configured);
    }

    [Fact]
    public async Task EscapeHatch_BypassesTheStoredConnectionString_SoTheFileValueWinsAgain()
    {
        await StoreBothSecretsAsync();

        Environment.SetEnvironmentVariable(
            DpapiSecretConfigurationProvider.IgnoreConnectionStringVariable, "true");

        var configuration = BuildConfiguration();

        configuration.GetConnectionString("AuthDb").Should().Be(FileConnectionString,
            "without this the API cannot start on a stale stored value, and cannot serve the console "
            + "that would fix it");
    }

    /// <summary>
    /// The hatch is deliberately narrow. Widening it to the whole secrets file
    /// would drop the signing keys too, and RequiredSecretsGuard would then refuse
    /// the boot it was meant to rescue.
    /// </summary>
    [Fact]
    public async Task EscapeHatch_LeavesEveryOtherSecretInPlace()
    {
        await StoreBothSecretsAsync();

        Environment.SetEnvironmentVariable(
            DpapiSecretConfigurationProvider.IgnoreConnectionStringVariable, "true");

        var configuration = BuildConfiguration();

        configuration["Email:Password"].Should().Be(StoredSmtpPassword);
        configuration["Jwt:PrivateKeyPem"].Should().NotBeNullOrWhiteSpace();
        configuration["Jwt:RefreshTokenHmacKeyPlain"].Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData(null)]
    public async Task EscapeHatch_AnythingOtherThanTrue_KeepsTheStoredConnectionString(string? value)
    {
        await StoreBothSecretsAsync();

        Environment.SetEnvironmentVariable(
            DpapiSecretConfigurationProvider.IgnoreConnectionStringVariable, value);

        var configuration = BuildConfiguration();

        configuration.GetConnectionString("AuthDb").Should().Be(StoredConnectionString);
    }
}
