using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Infrastructure.Authentication;
using Auth_API.Common.Authentication;
using Auth_API.Common.HealthChecks;
using Auth_API.Tests.SecretManagement;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.HealthChecks;

/// <summary>
/// X03: readiness proves the signing key signs. Until X03 it asked for the public key, which a
/// key holding only its public part still exports, so <c>/ready</c> read Healthy and the first
/// sign-in failed. S12: the key that signs must also be in the JWKS, a success is kept per key
/// (a promote in this process probes the new key), and a failure is never kept.
/// </summary>
public sealed class SigningKeyHealthCheckTests : IDisposable
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "https://auth.example.com",
        Audience = "auth-platform",
        KeyId = "test-key"
    };

    private readonly KeyringTestHarness _keyring = new();

    public void Dispose() => _keyring.Dispose();

    [Fact]
    public async Task CheckHealthAsync_APublicOnlyKey_IsUnhealthy()
    {
        using var keyPair = RSA.Create(2048);
        var publicOnly = RSA.Create();
        publicOnly.ImportFromPem(keyPair.ExportSubjectPublicKeyInfoPem());
        using var service = new JwtTokenService(Settings, publicOnly);

        var result = await Check(service, service.SigningKeys);

        service.GetPublicKeyPem().Should().NotBeNullOrWhiteSpace(
            "this is why asking for the public key proved nothing");
        result.Status.Should().Be(HealthStatus.Unhealthy, "a key that cannot sign cannot issue a single token");
    }

    [Fact]
    public async Task CheckHealthAsync_AKeyThatSigns_IsHealthy()
    {
        using var service = new JwtTokenService(Settings, RSA.Create(2048));

        var result = await Check(service, service.SigningKeys);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ACurrentKeyMissingFromTheJwks_IsUnhealthy_ThoughItSignsAndValidates()
    {
        // S12 TS9: a token is only of use to an application that can find its key.
        using var service = new JwtTokenService(Settings, RSA.Create(2048));
        var unpublished = new UnpublishedCurrentKey(service.SigningKeys);

        new SigningKeyProbe(Settings, unpublished).ProbeSigningKey().IsError.Should().BeFalse("the probe alone reads healthy");
        var result = await new SigningKeyHealthCheck(new SigningKeyProbe(Settings, unpublished), unpublished, new JwtTokenService(Settings, unpublished))
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not published in the JWKS");
    }

    [Fact]
    public void ProbeSigningKey_ASuccess_IsKeptForItsKey_SoReadinessCallsNeverSignAgain()
    {
        // /ready needs no sign-in: signing on every call would let any caller keep the server's
        // cores busy with RSA. While the key does not change, the answer is reused.
        using var service = new JwtTokenService(Settings, RSA.Create(2048));
        var counted = new CountingKeys(service.SigningKeys);
        var probe = new SigningKeyProbe(Settings, counted);

        probe.ProbeSigningKey().IsError.Should().BeFalse();
        var afterFirst = counted.Validations;
        probe.ProbeSigningKey().IsError.Should().BeFalse();
        probe.ProbeSigningKey().IsError.Should().BeFalse();

        afterFirst.Should().BeGreaterThan(0);
        counted.Validations.Should().Be(afterFirst, "a probe that signs validates; the second and third calls did neither");
    }

    [Fact]
    public void ProbeSigningKey_AFailure_IsNeverKept()
    {
        // OI-265 (b): a failure kept until a restart would read Unhealthy after the key was fixed.
        using var keyPair = RSA.Create(2048);
        var publicOnly = RSA.Create();
        publicOnly.ImportFromPem(keyPair.ExportSubjectPublicKeyInfoPem());
        using var service = new JwtTokenService(Settings, publicOnly);
        var probe = new SigningKeyProbe(Settings, service.SigningKeys);

        var first = Record.Exception(() => probe.ProbeSigningKey());
        var second = Record.Exception(() => probe.ProbeSigningKey());

        first.Should().NotBeNull();
        second.Should().NotBeNull().And.NotBeSameAs(first, "each call tries to sign again");
    }

    [Fact]
    public async Task ProbeSigningKey_AfterAPromoteInThisProcess_ProbesTheNewKey()
    {
        // A promote moves the signer to the staged key here; the answer kept for the old key
        // says nothing about the new one.
        await StagePublishAndRestartAsync();
        var counted = new CountingKeys(_keyring.SigningKeys);
        var probe = new SigningKeyProbe(Settings, counted);
        probe.ProbeSigningKey().IsError.Should().BeFalse();
        var oldKid = _keyring.SigningKeys.Current.Key.KeyId;
        var beforePromote = counted.Validations;

        (await _keyring.SigningService.ChangeAsync(
            _keyring.SigningRequest(Auth.Domain.Enums.KeyringAction.Promote, _keyring.ApproveSigning(Auth.Domain.Enums.KeyringAction.Promote)),
            newKey: null,
            (ring, change) => ring.Promote(change.NowUtc, change.PreviousVerifyUntilUtc),
            Auth.Application.Features.Secrets.Common.RsaKeyringChangeService.WithStagedKeyLoaded(
                (ring, change) => ring.Promote(change.NowUtc, change.PreviousVerifyUntilUtc)),
            CancellationToken.None)).IsError.Should().BeFalse();

        _keyring.SigningKeys.Current.Key.KeyId.Should().NotBe(oldKid);
        probe.ProbeSigningKey().IsError.Should().BeFalse();
        counted.Validations.Should().BeGreaterThan(beforePromote, "the new current key was signed with and validated");
    }

    private async Task StagePublishAndRestartAsync()
    {
        (await _keyring.SigningService.ChangeAsync(
            _keyring.SigningRequest(Auth.Domain.Enums.KeyringAction.Stage, _keyring.ApproveSigning(Auth.Domain.Enums.KeyringAction.Stage)),
            KeyringTestHarness.NewSigningKey(),
            (ring, change) => ring.Stage(change.NewKey!, change.NowUtc),
            applyInProcess: null,
            CancellationToken.None)).IsError.Should().BeFalse();
        await _keyring.RestartAsync();
        _keyring.Clock.Advance(TimeSpan.FromHours(25));
    }

    private static Task<HealthCheckResult> Check(JwtTokenService service, ISigningKeyMaterial keys) =>
        new SigningKeyHealthCheck(new SigningKeyProbe(Settings, keys), keys, service).CheckHealthAsync(new HealthCheckContext());

    /// <summary>The same keys, with the current one left out of what is published.</summary>
    private sealed class UnpublishedCurrentKey(ISigningKeyMaterial inner) : ISigningKeyMaterial
    {
        public SigningCredentials Current => inner.Current;

        public IReadOnlyList<SecurityKey> VerificationKeys(DateTimeOffset now) => inner.VerificationKeys(now);

        public IReadOnlyList<RsaSecurityKey> PublishedKeys(DateTimeOffset now) =>
            inner.PublishedKeys(now).Where(key => key.KeyId != inner.Current.Key.KeyId).ToArray();
    }

    /// <summary>The same keys, counting how often a token was validated against them.</summary>
    private sealed class CountingKeys(ISigningKeyMaterial inner) : ISigningKeyMaterial
    {
        public int Validations { get; private set; }

        public SigningCredentials Current => inner.Current;

        public IReadOnlyList<SecurityKey> VerificationKeys(DateTimeOffset now)
        {
            Validations++;
            return inner.VerificationKeys(now);
        }

        public IReadOnlyList<RsaSecurityKey> PublishedKeys(DateTimeOffset now) => inner.PublishedKeys(now);
    }
}
