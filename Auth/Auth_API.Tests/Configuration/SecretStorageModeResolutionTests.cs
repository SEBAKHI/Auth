using Auth.Shared.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// Unit tests for the Development fallback applied to the secret storage mode, and for
/// the appsettings file that PlainText-mode secrets are written to. Certificate mode is
/// the shipped default so real deployments encrypt the key ring at rest; the fallback
/// exists only so a fresh clone can boot, and must never soften a deployed environment.
/// </summary>
public class SecretStorageModeResolutionTests
{
    private static DataProtectionCertificateSettings WithPfx() =>
        new() { PfxPath = @"C:\certs\dp-cert.pfx", Password = "pw" };

    private static DataProtectionCertificateSettings WithThumbprint() =>
        new() { Thumbprint = "AB CD EF 12 34" };

    private static DataProtectionCertificateSettings NoSource() => new();

    [Fact]
    public void Development_CertificateWithoutCertificate_FallsBackToPlainText()
    {
        var mode = AuthDataProtectionExtensions.ResolveStorageMode(
            "Certificate", isDevelopment: true, NoSource());

        mode.Should().Be(SecretStorageMode.PlainText);
    }

    [Fact]
    public void Development_CertificateWithPfx_KeepsCertificate()
    {
        // A developer who provisions a certificate gets the production path locally.
        var mode = AuthDataProtectionExtensions.ResolveStorageMode(
            "Certificate", isDevelopment: true, WithPfx());

        mode.Should().Be(SecretStorageMode.Certificate);
    }

    [Fact]
    public void Development_CertificateWithThumbprint_KeepsCertificate()
    {
        var mode = AuthDataProtectionExtensions.ResolveStorageMode(
            "Certificate", isDevelopment: true, WithThumbprint());

        mode.Should().Be(SecretStorageMode.Certificate);
    }

    [Fact]
    public void NonDevelopment_CertificateWithoutCertificate_KeepsCertificate()
    {
        // The whole point of the gate: Production must still fail fast in
        // ConfigureKeyProtection rather than silently drop to plaintext.
        var mode = AuthDataProtectionExtensions.ResolveStorageMode(
            "Certificate", isDevelopment: false, NoSource());

        mode.Should().Be(SecretStorageMode.Certificate);
    }

    [Fact]
    public void NonDevelopment_CertificateWithNullSettings_KeepsCertificate()
    {
        var mode = AuthDataProtectionExtensions.ResolveStorageMode(
            "Certificate", isDevelopment: false, certificateSettings: null);

        mode.Should().Be(SecretStorageMode.Certificate);
    }

    [Theory]
    [InlineData("Dpapi", SecretStorageMode.Dpapi)]
    [InlineData("PlainText", SecretStorageMode.PlainText)]
    public void Development_NonCertificateModes_AreUnchanged(string configured, SecretStorageMode expected)
    {
        // Dpapi needs no certificate, so the fallback must not touch it.
        var mode = AuthDataProtectionExtensions.ResolveStorageMode(
            configured, isDevelopment: true, NoSource());

        mode.Should().Be(expected);
    }

    [Fact]
    public void ResolveTargetFile_WhenNotConfigured_UsesTheEnvironmentsLocalFile()
    {
        // Must be the environment's OWN layer (the old fixed appsettings.Production.json
        // is never read back in Development, so keys were regenerated on every restart,
        // invalidating live tokens) and specifically the git-ignored .local one, so
        // generated key material cannot land in the committed environment file.
        PlainTextSecretInitializer.ResolveTargetFile(null, "Development")
            .Should().Be("appsettings.Development.local.json");
    }

    [Fact]
    public void ResolveTargetFile_WhenEmpty_UsesTheEnvironmentsLocalFile()
    {
        // Guards the shipped appsettings.json value, which is "".
        PlainTextSecretInitializer.ResolveTargetFile("   ", "Staging")
            .Should().Be("appsettings.Staging.local.json");
    }

    [Fact]
    public void ResolveTargetFile_NeverTargetsACommittedEnvironmentFile()
    {
        // The committed appsettings.{Environment}.json must never receive key material.
        foreach (var environment in new[] { "Development", "Staging", "Production" })
        {
            PlainTextSecretInitializer.ResolveTargetFile(null, environment)
                .Should().NotBe($"appsettings.{environment}.json")
                .And.Be(LocalConfigurationExtensions.LocalFileName(environment));
        }
    }

    [Fact]
    public void ResolveTargetFile_WhenConfigured_IsHonoured()
    {
        PlainTextSecretInitializer.ResolveTargetFile(@"D:\secrets\keys.json", "Development")
            .Should().Be(@"D:\secrets\keys.json");
    }
}
