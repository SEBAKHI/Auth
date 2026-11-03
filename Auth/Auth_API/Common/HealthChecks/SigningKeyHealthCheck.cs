using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Common.HealthChecks;

/// <summary>
/// Readiness check that verifies the JWT signing key can sign: it signs a probe token and
/// validates it (<see cref="ISigningKeyProbe"/>), and the key that signs is listed in the JWKS
/// relying parties fetch (S12).
/// </summary>
/// <remarks>
/// The process can be alive yet unable to issue or validate tokens if the RSA key failed to load
/// or decrypt (e.g. a misconfigured secrets file, a missing Data Protection key ring, or a
/// PlainText key that was never generated). In that state the Auth API is running but cannot serve
/// its core purpose, so it must report <b>unhealthy for readiness</b> while still being "live".
/// <para>
/// Until X03 this asked for the public key, which a key holding only its public part still
/// exports: such a key read Healthy and the first sign-in failed.
/// </para>
/// <para>
/// A token this server signs is only of use to an application that can find its key: since the
/// keyring (S12) the check also reads the JWKS document the server publishes and requires the
/// current key's <c>kid</c> in it.
/// </para>
/// </remarks>
public sealed class SigningKeyHealthCheck : IHealthCheck
{
    private readonly ISigningKeyProbe _signingKeyProbe;
    private readonly ISigningKeyMaterial _signingKeys;
    private readonly IJwtTokenService _tokenService;

    public SigningKeyHealthCheck(
        ISigningKeyProbe signingKeyProbe,
        ISigningKeyMaterial signingKeys,
        IJwtTokenService tokenService)
    {
        _signingKeyProbe = signingKeyProbe;
        _signingKeys = signingKeys;
        _tokenService = tokenService;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var probe = _signingKeyProbe.ProbeSigningKey();
            if (probe.IsError)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"JWT signing key signed a token that did not validate ({probe.FirstError.Code})."));
            }

            var kid = _signingKeys.Current.Key.KeyId;
            return Task.FromResult(PublishedKeyIds(_tokenService.GetJwks()).Contains(kid ?? string.Empty)
                ? HealthCheckResult.Healthy("JWT signing key signs and validates, and is published in the JWKS.")
                : HealthCheckResult.Unhealthy($"The JWT signing key {kid} is not published in the JWKS."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("JWT signing key cannot sign.", ex));
        }
    }

    // The kids the JWKS document lists, read from the document itself: what an application sees.
    private static HashSet<string> PublishedKeyIds(string jwks)
    {
        using var document = JsonDocument.Parse(jwks);
        return document.RootElement.GetProperty("keys")
            .EnumerateArray()
            .Select(key => key.TryGetProperty("kid", out var kid) ? kid.GetString() : null)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }
}
