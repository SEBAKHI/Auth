using Auth.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Common.HealthChecks;

/// <summary>
/// Readiness check that verifies the JWT signing key material is loaded and usable.
/// </summary>
/// <remarks>
/// The process can be alive yet unable to issue or validate tokens if the RSA key failed to load
/// or decrypt (e.g. a misconfigured secrets file, a missing Data Protection key ring, or a
/// PlainText key that was never generated). In that state the Auth API is running but cannot serve
/// its core purpose, so it must report <b>unhealthy for readiness</b> while still being "live".
/// </remarks>
public sealed class SigningKeyHealthCheck : IHealthCheck
{
    private readonly IJwtTokenService _jwtTokenService;

    public SigningKeyHealthCheck(IJwtTokenService jwtTokenService)
    {
        _jwtTokenService = jwtTokenService;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // GetPublicKeyPem() exercises the loaded RSA key pair; it throws or returns empty if the
            // signing key is not available, which is exactly the failure we want readiness to catch.
            var publicKeyPem = _jwtTokenService.GetPublicKeyPem();

            return string.IsNullOrWhiteSpace(publicKeyPem)
                ? Task.FromResult(HealthCheckResult.Unhealthy("JWT signing key is not available (empty public key)."))
                : Task.FromResult(HealthCheckResult.Healthy("JWT signing key is loaded and usable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("JWT signing key could not be loaded.", ex));
        }
    }
}
