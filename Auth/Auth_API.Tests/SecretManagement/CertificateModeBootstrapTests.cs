using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Auth_API.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// Exercises the Certificate storage mode end to end — the mode production runs
/// in, and the only one where the original defect existed.
///
/// <para>
/// Every other test in this project runs the PlainText path, which is exactly
/// why a missing secret in the encrypted-file generator survived a full green
/// suite and reached production. These tests walk the real bootstrap sequence:
/// generate a certificate, protect a Data Protection key ring with it, run the
/// encrypted-file generator, layer the secrets file onto a configuration, and
/// assert the startup guard is satisfied.
/// </para>
///
/// <para>
/// The certificate is generated in-process and written to a temp .pfx that is
/// deleted afterwards. Nothing is read from the machine certificate store and
/// no fixture is committed, so this runs identically on a developer box and in
/// CI.
/// </para>
/// </summary>
public class CertificateModeBootstrapTests : IDisposable
{
    private const string PfxPassword = "test-only-not-a-secret";

    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(), "authsystem-cert-tests", Guid.NewGuid().ToString("N"));

    private readonly DataProtectionCertificateSettings _certificateSettings;

    public CertificateModeBootstrapTests()
    {
        Directory.CreateDirectory(_workingDirectory);

        var pfxPath = Path.Combine(_workingDirectory, "dataprotection.pfx");
        File.WriteAllBytes(pfxPath, CreateSelfSignedPfx());

        _certificateSettings = new DataProtectionCertificateSettings
        {
            PfxPath = pfxPath,
            Password = PfxPassword
        };
    }

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
    public void CertificateMode_IsResolved_WhenACertificateIsConfigured()
    {
        var resolved = AuthDataProtectionExtensions.ResolveStorageMode(
            "Certificate", isDevelopment: false, _certificateSettings);

        resolved.Should().Be(SecretStorageMode.Certificate,
            "a configured certificate must not silently fall back to PlainText outside Development — " +
            "ProductionSecretGuard would then refuse the plaintext secrets it generates");
    }

    [Fact]
    public async Task FirstStartup_ProvisionsEverySecret_AndSatisfiesTheStartupGuard()
    {
        var secretFilePath = Path.Combine(_workingDirectory, "secrets.dpapi");
        var settings = new SecretManagementSettings
        {
            SecretFilePath = secretFilePath,
            AutoGenerateKeys = true
        };

        // --- the Program.cs first-startup branch, verbatim in shape ---
        var provider = BuildCertificateProtectedProvider();
        var service = new DpapiSecretService(
            provider, Options.Create(settings), NullLogger<DpapiSecretService>.Instance);

        File.Exists(secretFilePath).Should().BeFalse("this models a first startup");
        await service.GenerateMissingKeysAsync(CancellationToken.None);

        // --- the configuration layer the process actually reads ---
        var configuration = new ConfigurationBuilder()
            .AddDpapiSecrets(provider, secretFilePath)
            .Build();

        var act = () => RequiredSecretsGuard.EnsureAllPresent(configuration, "Certificate");

        act.Should().NotThrow(
            "a first startup in Certificate mode must leave the process able to serve traffic; " +
            "before the fix it booted with AccountDeletion:IdentifierHmacKeyPlain absent and returned " +
            "500 on every registration attempt");
    }

    [Fact]
    public async Task ExistingSecretFile_MissingOnlyTheLateAddedKey_IsToppedUpWithoutTouchingTheOthers()
    {
        // Models the real production state: a secrets file created before the
        // identifier key existed. The top-up must add that one key and leave
        // every other secret byte-identical — re-minting the gateway token here
        // would lock the API Gateway out, and re-minting the JWT key would
        // invalidate every issued token.
        var secretFilePath = Path.Combine(_workingDirectory, "legacy-secrets.dpapi");
        var settings = new SecretManagementSettings
        {
            SecretFilePath = secretFilePath,
            AutoGenerateKeys = true
        };

        var provider = BuildCertificateProtectedProvider();
        var service = new DpapiSecretService(
            provider, Options.Create(settings), NullLogger<DpapiSecretService>.Instance);

        await service.GenerateMissingKeysAsync(CancellationToken.None);

        var legacy = await service.LoadSecretsAsync(CancellationToken.None);
        var jwtBefore = legacy.JwtPrivateKeyPem;
        var hmacBefore = legacy.RefreshTokenHmacKey;
        var gatewayBefore = legacy.GatewayToken;

        // Rewind to the pre-fix shape of the file.
        legacy.AccountDeletionIdentifierHmacKey = null;
        await service.SaveSecretsAsync(legacy, CancellationToken.None);

        await service.GenerateMissingKeysAsync(CancellationToken.None);
        var toppedUp = await service.LoadSecretsAsync(CancellationToken.None);

        toppedUp.AccountDeletionIdentifierHmacKey.Should().NotBeNullOrWhiteSpace(
            "the late-added key is the whole point of the top-up");
        toppedUp.JwtPrivateKeyPem.Should().Be(jwtBefore, "re-minting it invalidates every access token");
        toppedUp.RefreshTokenHmacKey.Should().Be(hmacBefore, "re-minting it invalidates every refresh token");
        toppedUp.GatewayToken.Should().Be(gatewayBefore, "re-minting it rejects 100% of proxied requests");
    }

    [Fact]
    public async Task TheIdentifierKey_SurvivesARestart_Unchanged()
    {
        // The key is permanent: two consecutive startups against the same file
        // must resolve the same value, or every stored reservation is orphaned.
        var secretFilePath = Path.Combine(_workingDirectory, "restart-secrets.dpapi");
        var settings = new SecretManagementSettings
        {
            SecretFilePath = secretFilePath,
            AutoGenerateKeys = true
        };

        var provider = BuildCertificateProtectedProvider();

        var firstBoot = new DpapiSecretService(
            provider, Options.Create(settings), NullLogger<DpapiSecretService>.Instance);
        await firstBoot.GenerateMissingKeysAsync(CancellationToken.None);
        var first = new ConfigurationBuilder().AddDpapiSecrets(provider, secretFilePath).Build();

        var secondBoot = new DpapiSecretService(
            provider, Options.Create(settings), NullLogger<DpapiSecretService>.Instance);
        await secondBoot.GenerateMissingKeysAsync(CancellationToken.None);
        var second = new ConfigurationBuilder().AddDpapiSecrets(provider, secretFilePath).Build();

        second["AccountDeletion:IdentifierHmacKeyPlain"]
            .Should().Be(first["AccountDeletion:IdentifierHmacKeyPlain"],
                "a permanent key that changes across restarts silently orphans the deletion registry, " +
                "making identifiers the policy promises are never recycled registrable again");

        IdentifierKeyRegenerationGuard.Fingerprint(second["AccountDeletion:IdentifierHmacKeyPlain"])
            .Should().Be(IdentifierKeyRegenerationGuard.Fingerprint(first["AccountDeletion:IdentifierHmacKeyPlain"]),
                "the logged fingerprint is how an operator detects exactly this failure");
    }

    [Fact]
    public void TheGeneratedKey_IsAcceptedByTheHasherAndIsStable()
    {
        var settings = new AccountDeletionSettings
        {
            IdentifierHmacKeyPlain = KeyMaterialGenerator.GenerateHmacKeyBase64()
        };

        var hasher = new IdentifierHasher(Options.Create(settings));

        // Generated material must satisfy the hasher's own >= 32-byte rule, and
        // hashing must be case- and whitespace-stable or the reservation lookup
        // misses on a differently-typed address.
        hasher.HashEmail(" User@Example.COM ").Should().Be(hasher.HashEmail("user@example.com"));
    }

    private IDataProtectionProvider BuildCertificateProtectedProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("AuthSystem")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_workingDirectory, "keys")))
            .ConfigureKeyProtection(SecretStorageMode.Certificate, _certificateSettings);

        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    /// <summary>
    /// Self-signed certificate with an exportable private key, generated in
    /// process. No machine store, no committed fixture, no operator step.
    /// </summary>
    private static byte[] CreateSelfSignedPfx()
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=AuthSystem Data Protection Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment,
                critical: false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        return certificate.Export(X509ContentType.Pfx, PfxPassword);
    }
}
