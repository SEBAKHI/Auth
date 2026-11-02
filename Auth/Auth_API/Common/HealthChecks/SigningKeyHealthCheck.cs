using Auth.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Common.HealthChecks;

/// <summary>
/// Readiness check that verifies the JWT signing key can sign: it signs a probe token and
/// validates it (<see cref="ISigningKeyProbe"/>).
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
/// </remarks>
public sealed class SigningKeyHealthCheck : IHealthCheck
{
    private readonly ISigningKeyProbe _signingKeyProbe;

    public SigningKeyHealthCheck(ISigningKeyProbe signingKeyProbe)
    {
        _signingKeyProbe = signingKeyProbe;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var probe = _signingKeyProbe.ProbeSigningKey();

            return probe.IsError
                ? Task.FromResult(HealthCheckResult.Unhealthy(
                    $"JWT signing key signed a token that did not validate ({probe.FirstError.Code})."))
                : Task.FromResult(HealthCheckResult.Healthy("JWT signing key signs and validates."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("JWT signing key cannot sign.", ex));
        }
    }
}
