using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S11 (KR5 item 10): the API Gateway decrypts the whole secrets file but keeps only
/// its own token, so the signing key and the HMAC keyring never sit in its memory.
/// </summary>
public class GatewaySecretProviderFilterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "authsystem-gateway-filter-tests", Guid.NewGuid().ToString("N"));
    private readonly EphemeralDataProtectionProvider _protection = new();

    public GatewaySecretProviderFilterTests() => Directory.CreateDirectory(_directory);

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

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TheGatewayFilter_TakesTheGatewayToken_AndNothingElse()
    {
        var path = await WriteAFullSecretsFileAsync();

        var all = new ConfigurationBuilder().AddDpapiSecrets(_protection, path).Build();
        var gateway = new ConfigurationBuilder().AddDpapiSecrets(_protection, path, DpapiSecretFilters.GatewayTokenOnly).Build();

        Populated(all).Should().Contain(["Jwt:PrivateKeyPem", "Jwt:RefreshTokenHmacKeyPlain", "Keyring:Hmac:Keys:1:Material", "Gateway:Token"],
            "the unfiltered provider maps the whole file, so the filter below is what keeps the rest out");
        Populated(gateway).Should().Equal("Gateway:Token");
        gateway["Gateway:Token"].Should().Be(all["Gateway:Token"]);
    }

    [Fact]
    public void TheFilter_KeepsEverySecretTheGatewayRequires()
    {
        RequiredSecretsRegistry.Gateway.Select(secret => secret.ConfigurationKey)
            .Should().OnlyContain(key => DpapiSecretFilters.GatewayTokenOnly.Contains(key));
    }

    [Fact]
    public void TheGateway_LoadsTheSecretsFileThroughTheFilter()
    {
        var program = File.ReadAllText(Path.Combine(SolutionDirectory(), "API_Gateway", "Program.cs"));
        var calls = Regex.Matches(program, @"AddDpapiSecrets\((?<args>[^;]*)\);");

        calls.Should().NotBeEmpty();
        calls.Should().OnlyContain(call => call.Groups["args"].Value.Contains("DpapiSecretFilters.GatewayTokenOnly"),
            "every load of the secrets file in the gateway keeps only its token");
    }

    private async Task<string> WriteAFullSecretsFileAsync()
    {
        var path = Path.Combine(_directory, "secrets.dpapi");
        var service = new DpapiSecretService(
            _protection,
            Options.Create(new SecretManagementSettings { SecretFilePath = path, AutoGenerateKeys = true }),
            NullLogger<DpapiSecretService>.Instance);

        await service.GenerateMissingKeysAsync(CancellationToken.None);
        await service.SetSecretAsync("SmtpPassword", "smtp-password", CancellationToken.None);
        await service.SetSecretAsync("ConnectionStrings.AuthDb", "Server=example;Database=Auth", CancellationToken.None);
        return path;
    }

    private static List<string> Populated(IConfiguration configuration) =>
        configuration.AsEnumerable()
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .Select(pair => pair.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must be able to locate Auth.sln");
        return directory!.FullName;
    }
}
