using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S11 SEC-1: wherever secrets live in the encrypted file, the HMAC keyring is taken from
/// that file alone. An environment variable (or any other source) layered above the file
/// could otherwise add a key that verifies credentials, or decide which key is current,
/// once the ring holds more than one key. The boot refuses it, in every environment.
/// </summary>
public sealed class HmacKeyringConfigurationSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "authsystem-keyring-source", Guid.NewGuid().ToString("N"));
    private readonly string _prefix = "S11SRC" + Guid.NewGuid().ToString("N")[..8] + "_";
    private readonly EphemeralDataProtectionProvider _protection = new();
    private readonly List<string> _variables = [];

    public HmacKeyringConfigurationSourceTests()
    {
        Directory.CreateDirectory(_directory);
        new DpapiSecretService(
                _protection,
                Options.Create(new SecretManagementSettings { SecretFilePath = FilePath, AutoGenerateKeys = true }),
                NullLogger<DpapiSecretService>.Instance)
            .GenerateMissingKeysAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private string FilePath => Path.Combine(_directory, "secrets.dpapi");

    public void Dispose()
    {
        foreach (var variable in _variables)
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

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
    public void ARingKeyFromAnEnvironmentVariable_RefusesTheBoot_WhenSecretsLiveInTheFile()
    {
        SetVariable("Keyring__Hmac__Keys__2__Material", KeyMaterialGenerator.GenerateHmacKeyBase64());
        SetVariable("Keyring__Hmac__Keys__2__DemotedAtUtc", "2026-11-01T00:00:00Z");

        var load = () => HmacKeyringConfiguration.Load(Configuration(), secretFileOnly: true);

        load.Should().Throw<InvalidOperationException>()
            .WithMessage("*only come from the encrypted secrets file*Keyring:Hmac:Keys:2:Material*")
            .Which.Message.Should().NotContain(Environment.GetEnvironmentVariable(_prefix + "Keyring__Hmac__Keys__2__Material")!,
                "the refusal names the setting, never its value");
    }

    [Fact]
    public void WithoutTheCheck_TheEnvironmentKeyWouldVerify()
    {
        // What the check prevents: the variable becomes a previous key of the ring, and every
        // credential computed under it would be accepted.
        SetVariable("Keyring__Hmac__Keys__2__Material", KeyMaterialGenerator.GenerateHmacKeyBase64());
        SetVariable("Keyring__Hmac__Keys__2__DemotedAtUtc", "2026-11-01T00:00:00Z");

        var ring = HmacKeyringConfiguration.Load(Configuration(), secretFileOnly: false);

        ring.Find(2).Should().NotBeNull();
    }

    [Fact]
    public void ARingThatComesOnlyFromTheFile_Loads()
    {
        var ring = HmacKeyringConfiguration.Load(Configuration(), secretFileOnly: true);

        ring.Current.Id.Should().Be(1);
        ring.Keys.Should().ContainSingle();
    }

    [Fact]
    public void AnEmptyPlaceholder_InASettingsFile_IsNoValue()
    {
        var configuration = Configuration(new Dictionary<string, string?> { ["Keyring:Hmac:Keys:2:Material"] = "" });

        var load = () => HmacKeyringConfiguration.Load(configuration, secretFileOnly: true);

        load.Should().NotThrow("the settings files ship empty placeholders that mean 'not set'");
    }

    private IConfigurationRoot Configuration(IDictionary<string, string?>? settingsFile = null)
    {
        var builder = new ConfigurationBuilder()
            .AddInMemoryCollection(settingsFile ?? new Dictionary<string, string?>())
            .AddEnvironmentVariables(_prefix);
        Auth.Infrastructure.Configuration.SecretConfigurationExtensions.AddDpapiSecrets(builder, _protection, FilePath);
        return builder.Build();
    }

    private void SetVariable(string name, string value)
    {
        _variables.Add(_prefix + name);
        Environment.SetEnvironmentVariable(_prefix + name, value);
    }
}
