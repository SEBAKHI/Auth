using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// SecretConfiguration exists in BOTH Auth.Application.Configuration and
// Auth.Shared.Configuration with identical members. DpapiSecretService binds the
// Application copy, so that is the one reflected over here.
using SecretFile = Auth.Application.Configuration.SecretConfiguration;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// Both secret generators must provision every secret in
/// <see cref="RequiredSecretsRegistry"/>.
///
/// <para>
/// The bug these tests exist for: the account-deletion identifier HMAC key was
/// added to the PlainText generator and never to the encrypted-file generator.
/// Development generated it, Production could not, and the gap surfaced only as
/// a 500 on every signup in production — the reservation guard sits on all four
/// account-creation paths — while the whole suite stayed green because nothing
/// ever exercised Certificate/Dpapi provisioning.
/// </para>
///
/// <para>
/// These run the real generators against a temporary secrets file rather than
/// pattern-matching their source, so a generator that lists a secret but fails
/// to persist it still fails here.
/// </para>
/// </summary>
public class RequiredSecretsCoverageTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(), "authsystem-secret-tests", Guid.NewGuid().ToString("N"));

    public RequiredSecretsCoverageTests() => Directory.CreateDirectory(_workingDirectory);

    public void Dispose()
    {
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

    [Fact]
    public async Task EncryptedFileGenerator_ProvisionsEveryRequiredSecret()
    {
        var service = CreateEncryptedFileService(out _);

        await service.GenerateMissingKeysAsync(CancellationToken.None);
        var secrets = await service.LoadSecretsAsync(CancellationToken.None);

        var unprovisioned = RequiredSecretsRegistry.All
            .Where(secret => string.IsNullOrWhiteSpace(ReadProperty(secrets, secret.SecretFileProperty)))
            .Select(secret => $"{secret.SecretFileProperty} (for {secret.ConfigurationKey})")
            .ToList();

        unprovisioned.Should().BeEmpty(
            "GenerateMissingKeysAsync is the ONLY provisioning path in Certificate/Dpapi mode, which is the " +
            "mode production runs in — a required secret it does not generate simply never exists there");
    }

    [Fact]
    public async Task EncryptedFileGenerator_IsIdempotent_AndNeverRegeneratesAnExistingSecret()
    {
        var service = CreateEncryptedFileService(out _);

        await service.GenerateMissingKeysAsync(CancellationToken.None);
        var first = await service.LoadSecretsAsync(CancellationToken.None);
        var before = Snapshot(first);

        await service.GenerateMissingKeysAsync(CancellationToken.None);
        var second = await service.LoadSecretsAsync(CancellationToken.None);

        Snapshot(second).Should().BeEquivalentTo(before,
            "a second pass must skip every secret that already exists — regenerating the permanent " +
            "identifier key orphans the deletion registry, and regenerating the gateway token locks the " +
            "API Gateway out of the API");
    }

    [Fact]
    public void PlainTextGenerator_ProvisionsEveryRequiredSecret()
    {
        var result = PlainTextSecretInitializer.EnsureSecrets(
            new ConfigurationBuilder().Build(),
            _workingDirectory,
            autoGenerate: true,
            targetFile: "appsettings.Test.local.json");

        result.Generated.Should().BeTrue();

        var unprovisioned = RequiredSecretsRegistry.All
            .Where(secret => !result.ConfigValues.TryGetValue(secret.ConfigurationKey, out var value)
                             || string.IsNullOrWhiteSpace(value))
            .Select(secret => secret.ConfigurationKey)
            .ToList();

        unprovisioned.Should().BeEmpty(
            "the PlainText generator must cover the same set as the encrypted-file generator, otherwise " +
            "Development and Production disagree about which secrets exist");
    }

    [Fact]
    public void EveryRequiredSecret_IsMappedFromTheSecretFileIntoConfiguration()
    {
        // Generation is worthless if the value never reaches the configuration
        // key the process actually reads.
        var mapping = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Shared", "Configuration", "DpapiSecretConfigurationProvider.cs"));

        foreach (var secret in RequiredSecretsRegistry.All)
        {
            mapping.Should().Contain($"\"{secret.ConfigurationKey}\"",
                $"{secret.SecretFileProperty} must be mapped onto {secret.ConfigurationKey}");
            mapping.Should().Contain(secret.SecretFileProperty,
                $"{secret.ConfigurationKey} must be sourced from SecretConfiguration.{secret.SecretFileProperty}");
        }
    }

    [Fact]
    public void FindMissing_ReportsAbsentAndBlankValues()
    {
        var present = RequiredSecretsRegistry.All.ToDictionary(s => s.ConfigurationKey, _ => "value");
        RequiredSecretsRegistry.FindMissing(key => present.GetValueOrDefault(key)).Should().BeEmpty();

        present[RequiredSecretsRegistry.All[0].ConfigurationKey] = "   ";
        RequiredSecretsRegistry.FindMissing(key => present.GetValueOrDefault(key))
            .Should().ContainSingle(secret => secret.ConfigurationKey == RequiredSecretsRegistry.All[0].ConfigurationKey,
                "whitespace is not a provisioned secret");
    }

    [Fact]
    public void TheGatewayToken_IsDeclaredForBothProcesses_FromTheSameSecret()
    {
        // The gateway stamps the token and the API verifies it. They are two
        // configuration keys over ONE secret, so they must be generated from the
        // same SecretConfiguration property or the two processes disagree and
        // every proxied request is rejected while both look healthy.
        var gateway = RequiredSecretsRegistry.Gateway
            .Should().ContainSingle(s => s.ConfigurationKey == "Gateway:Token").Subject;
        var api = RequiredSecretsRegistry.All
            .Should().ContainSingle(s => s.ConfigurationKey == "Gateway:ExpectedToken").Subject;

        gateway.SecretFileProperty.Should().Be(api.SecretFileProperty,
            "both sides of the gateway handshake must come from the same generated secret");
    }

    [Fact]
    public void TheIdentifierHmacKey_IsDeclaredPermanent()
    {
        RequiredSecretsRegistry.All
            .Single(secret => secret.ConfigurationKey == "AccountDeletion:IdentifierHmacKeyPlain")
            .Permanence.Should().Be(SecretPermanence.Permanent,
                "the deletion registry's digests are derived from it, so a replacement key silently " +
                "orphans every reservation instead of failing");
    }

    private DpapiSecretService CreateEncryptedFileService(out SecretManagementSettings settings)
    {
        settings = new SecretManagementSettings
        {
            SecretFilePath = Path.Combine(_workingDirectory, "secrets.dpapi"),
            AutoGenerateKeys = true
        };

        return new DpapiSecretService(
            new EphemeralDataProtectionProvider(),
            Options.Create(settings),
            NullLogger<DpapiSecretService>.Instance);
    }

    private static Dictionary<string, string?> Snapshot(SecretFile secrets) =>
        RequiredSecretsRegistry.All.ToDictionary(
            secret => secret.SecretFileProperty,
            secret => ReadProperty(secrets, secret.SecretFileProperty));

    private static string? ReadProperty(SecretFile secrets, string propertyName)
    {
        var property = typeof(SecretFile).GetProperty(propertyName);
        property.Should().NotBeNull(
            $"RequiredSecretsRegistry names SecretConfiguration.{propertyName}, which must exist");

        return property!.GetValue(secrets) as string;
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
