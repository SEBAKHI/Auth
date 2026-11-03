using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Features.Discovery.GetJwks;
using Auth.Application.Features.Discovery.GetPublicKey;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Infrastructure.Authentication;
using Auth_API.Controllers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Auth_API.Tests.Discovery;

/// <summary>
/// S12, TS1: the key set changes nothing a relying party reads for the key it already has. For a
/// fixed key under the configured <c>kid</c>, the JWKS and the PEM are byte for byte what the code
/// before the key set produced (<c>8fef89b2</c>), on the service and on the wire.
/// </summary>
/// <remarks>
/// The two golden files under <c>Golden/</c> were written by that commit's own code from the
/// public key beside them, and are never regenerated from the code under test. Both bodies end
/// their lines with the platform's newline, as they always did; the files hold LF, and the test
/// compares against them with the platform's newline restored.
/// </remarks>
public sealed class JwksCompatibilityGoldenTests : IDisposable
{
    /// <summary>The <c>kid</c> every existing deployment publishes: <c>Jwt:KeyId</c>'s default.</summary>
    private const string ExistingKeyId = "auth-key-1";

    private readonly JwtTokenService _service = DiscoveryHost.ServiceOverTheGoldenKey(ExistingKeyId);

    public void Dispose() => _service.Dispose();

    [Fact]
    public void Jwks_OfTheExistingKey_IsByteForByteTheBodyBeforeTheKeySet()
    {
        _service.GetJwks().Should().Be(Golden("jwks.golden.json"));
    }

    [Fact]
    public void PublicKeyPem_OfTheExistingKey_IsByteForByteTheBodyBeforeTheKeySet()
    {
        _service.GetPublicKeyPem().Should().Be(Golden("public-key.golden.pem"));
    }

    [Fact]
    public async Task TheTwoEndpoints_ServeTheGoldenBodies()
    {
        using var host = await DiscoveryHost.StartAsync(_service);
        var client = host.GetTestClient();

        var jwks = await client.GetAsync("/.well-known/jwks.json");
        var pem = await client.GetAsync("/.well-known/public-key.pem");

        jwks.StatusCode.Should().Be(HttpStatusCode.OK);
        (await jwks.Content.ReadAsByteArrayAsync()).Should().Equal(Utf8(Golden("jwks.golden.json")));
        pem.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pem.Content.ReadAsByteArrayAsync()).Should().Equal(Utf8(Golden("public-key.golden.pem")));
    }

    private static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    private static string Golden(string name)
    {
        var text = File.ReadAllText(DiscoveryHost.GoldenPath(name)).Replace("\r\n", "\n");
        text.Should().NotBeNullOrWhiteSpace("{0} is the recorded body", name);
        return text.Replace("\n", Environment.NewLine);
    }
}

/// <summary>
/// S12: the JWKS answer may be reused for five minutes, by any cache. The header is the one thing
/// about the endpoint S12 changes; the PEM and a failed answer stay uncached.
/// </summary>
public sealed class JwksCacheControlTests : IDisposable
{
    private readonly JwtTokenService _service = DiscoveryHost.ServiceOverTheGoldenKey("auth-key-1");

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Jwks_IsCacheablePublicly_ForFiveMinutes()
    {
        using var host = await DiscoveryHost.StartAsync(_service);

        var response = await host.GetTestClient().GetAsync("/.well-known/jwks.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(300));
        response.Headers.CacheControl.ToString().Should().Be(DiscoveryController.JwksCacheControl);
        DiscoveryController.JwksCacheControl.Should().Be("public, max-age=300");
    }

    [Fact]
    public async Task AFailedJwks_IsNotCached()
    {
        using var host = await DiscoveryHost.StartAsync(_service, failJwks: true);

        var response = await host.GetTestClient().GetAsync("/.well-known/jwks.json");

        response.IsSuccessStatusCode.Should().BeFalse();
        response.Headers.CacheControl.Should().BeNull("a failure must never be kept for five minutes");
    }

    [Fact]
    public async Task ThePem_IsLeftAsItWas()
    {
        using var host = await DiscoveryHost.StartAsync(_service);

        var response = await host.GetTestClient().GetAsync("/.well-known/public-key.pem");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().BeNull();
    }

    [Fact]
    public void TheMigratedRing_ReadFromTheSecretsFile_PublishesExactlyWhatTheOneKeySetPublishes()
    {
        // S12 PR B, TS1: a deployment that never rotated loads its one key as the migrated ring,
        // through the file reader the start uses. Its bodies are those of the one-key set, whose
        // bytes the tests above hold to the golden files.
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var migrated = RsaKeyringFile.FromEntries(null, null, null, pem, [], RsaKeyring.MigratedKeyId).Value;
        var settings = new JwtSettings { Issuer = "https://auth.example.com", Audience = "authsystem-api", KeyId = RsaKeyring.MigratedKeyId };

        using var fromTheRing = new JwtTokenService(settings, SigningKeyRing.Load(migrated, new SigningKeySource(SigningKeyLoader.PemKey, PlainText: false)));
        using var fromOneKey = new JwtTokenService(settings, SigningKeyRing.Single(rsa, RsaKeyring.MigratedKeyId, new SigningKeySource(SigningKeyLoader.PemKey, PlainText: true)));

        migrated.Persisted.Should().BeFalse();
        fromTheRing.GetJwks().Should().Be(fromOneKey.GetJwks());
        fromTheRing.GetPublicKeyPem().Should().Be(fromOneKey.GetPublicKeyPem());
    }

    [Fact]
    public void WithFourKeys_TheCurrentKeyComesFirst_AndNoPrivateMemberIsPublished()
    {
        var now = DateTime.UtcNow;
        var keys = Enumerable.Range(0, 4).Select(_ => Auth_API.Tests.SecretManagement.KeyringTestHarness.NewSigningKey() with { CreatedAtUtc = now.AddDays(-1) }).ToList();
        var ring = RsaKeyring.Create(
            [
                keys[0],
                keys[1] with { PublishedAtUtc = now },
                keys[2] with { PrivateKeyPem = null, VerifyUntilUtc = now.AddHours(10) },
                keys[3] with { PrivateKeyPem = null, VerifyUntilUtc = now.AddHours(20) }
            ],
            keys[0].Kid,
            keys[1].Kid,
            removed: null).Value;
        using var service = new JwtTokenService(
            new JwtSettings { Issuer = "https://auth.example.com", Audience = "authsystem-api" },
            SigningKeyRing.Load(ring, new SigningKeySource(SigningKeyLoader.PemKey, PlainText: false)));

        using var jwks = System.Text.Json.JsonDocument.Parse(service.GetJwks());
        var published = jwks.RootElement.GetProperty("keys").EnumerateArray().ToList();

        published.Select(key => key.GetProperty("kid").GetString())
            .Should().Equal(keys[0].Kid, keys[1].Kid, keys[3].Kid, keys[2].Kid);
        published.Should().OnlyContain(key =>
            key.EnumerateObject().Select(member => member.Name).SequenceEqual(new[] { "kty", "use", "alg", "kid", "n", "e" }),
            "the members, in today's order, and never d, p, q, dp, dq or qi");
        service.GetPublicKeyPem().Should().Be(PublicPem(keys[0]), "the PEM is the current key");
    }

    private static string PublicPem(SigningKey key)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PublicKeyPem);
        var body = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        var lines = Enumerable.Range(0, (body.Length + 63) / 64).Select(i => body.Substring(i * 64, Math.Min(64, body.Length - i * 64)));
        return string.Concat(new[] { "-----BEGIN RSA PUBLIC KEY-----" }.Concat(lines).Append("-----END RSA PUBLIC KEY-----").Select(line => line + Environment.NewLine));
    }
}

/// <summary>The discovery controller on a test server, over the real query handlers.</summary>
internal static class DiscoveryHost
{
    public static string GoldenPath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run inside the solution tree");
        return Path.Combine(directory!.FullName, "Auth_API.Tests", "Discovery", "Golden", name);
    }

    /// <summary>A token service over the recorded public key: the JWKS and the PEM need no private part.</summary>
    public static JwtTokenService ServiceOverTheGoldenKey(string keyId)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(GoldenPath("signing-key.public.pem")));
        return new JwtTokenService(
            new JwtSettings { Issuer = "https://auth.example.com", Audience = "authsystem-api", KeyId = keyId },
            rsa);
    }

    public static async Task<IHost> StartAsync(IJwtTokenService service, bool failJwks = false)
    {
        var jwks = new GetJwksQueryHandler(service);
        var pem = new GetPublicKeyQueryHandler(service);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetJwksQuery>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ErrorOr<string>> query, CancellationToken cancellationToken) => failJwks
                ? Task.FromResult<ErrorOr<string>>(Error.Unexpected("Test.JwksUnavailable", "The key set could not be read."))
                : jwks.Handle((GetJwksQuery)query, cancellationToken));
        sender.Setup(s => s.Send(It.IsAny<GetPublicKeyQuery>(), It.IsAny<CancellationToken>()))
            .Returns((IRequest<ErrorOr<string>> query, CancellationToken cancellationToken) =>
                pem.Handle((GetPublicKeyQuery)query, cancellationToken));

        return await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(sender.Object);
                    services.Configure<IdentityProviderSettings>(_ => { });
                    services.AddAuthorization();
                    services.AddProblemDetails();
                    services.AddControllers()
                        .ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.FeatureProviders.Add(new DiscoveryControllerOnly());
                        });
                    services.AddApiVersioning().AddMvc();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .StartAsync();
    }

    private sealed class DiscoveryControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(DiscoveryController).GetTypeInfo());
        }
    }
}
