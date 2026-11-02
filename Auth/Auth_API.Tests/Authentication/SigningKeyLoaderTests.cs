using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X03: the signing key is checked when it loads. Until X03 the winning source was imported
/// unchecked, a public-only or 1024-bit PEM loaded, and no source at all produced an unsaved random
/// key that lived until the next restart.
/// </summary>
public class SigningKeyLoaderTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(), "authsystem-signing-key-tests", Guid.NewGuid().ToString("N"));

    public SigningKeyLoaderTests()
    {
        Directory.CreateDirectory(_workingDirectory);
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
    public void Load_A2048BitPrivateKey_LoadsAKeyThatSigns()
    {
        using var rsa = SigningKeyLoader.Load(new JwtSettings { PrivateKeyPem = TestSigningKeys.NewPrivateKeyPem() }, null);

        rsa.KeySize.Should().Be(2048);
        rsa.SignData([1, 2, 3], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Should().NotBeEmpty();
    }

    [Fact]
    public void Load_APublicOnlyKey_RefusesNamingTheSource()
    {
        using var rsa = RSA.Create(2048);

        var act = () => SigningKeyLoader.Load(new JwtSettings { PrivateKeyPem = rsa.ExportSubjectPublicKeyInfoPem() }, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*the JWT signing key from Jwt:PrivateKeyPem cannot sign tokens*",
                "a public key exports a public key and signs nothing: the first sign-in would fail");
    }

    [Fact]
    public void Load_A1024BitKey_Refuses()
    {
        using var rsa = RSA.Create(1024);

        var act = () => SigningKeyLoader.Load(new JwtSettings { PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:PrivateKeyPem*below the required 2048-bit minimum*");
    }

    [Fact]
    public void Load_NoSource_Refuses()
    {
        var act = () => SigningKeyLoader.Load(new JwtSettings(), null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no JWT signing key is configured*",
                "an unsaved random key signs tokens nobody can validate after the next restart");
    }

    [Fact]
    public void Load_AKeyFile_WinsOverThePemAndIsChecked()
    {
        using var weak = RSA.Create(1024);
        var path = Path.Combine(_workingDirectory, "signing.pem");
        File.WriteAllText(path, weak.ExportPkcs8PrivateKeyPem());

        var act = () => SigningKeyLoader.Load(
            new JwtSettings { PrivateKeyPath = path, PrivateKeyPem = TestSigningKeys.NewPrivateKeyPem() }, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*from Jwt:PrivateKeyPath cannot sign tokens*", "the file is the source that signs, so it is the one checked");
    }

    [Fact]
    public void Load_APathWithNoFile_FallsThroughToThePem()
    {
        // The loader keeps the long-standing order; outside Development the startup check
        // (KeyMaterialValidator) refuses this state before the loader runs.
        using var rsa = SigningKeyLoader.Load(
            new JwtSettings
            {
                PrivateKeyPath = Path.Combine(_workingDirectory, "missing.pem"),
                PrivateKeyPem = TestSigningKeys.NewPrivateKeyPem()
            },
            null);

        rsa.KeySize.Should().Be(2048);
    }

    [Fact]
    public void JwtTokenService_WithNoKeyConfigured_Refuses()
    {
        var act = () => new JwtTokenService(Options.Create(new JwtSettings()), Mock.Of<IPasswordHasher>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no JWT signing key is configured*");
    }
}
