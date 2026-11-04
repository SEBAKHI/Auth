using Auth.Infrastructure.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Common.HealthChecks;

/// <summary>
/// Readiness check that the token revocation list has been loaded from durable storage
/// since the process started (X04, RV2 (e)).
/// </summary>
/// <remarks>
/// Until a load succeeds, an access token revoked before this start (a sign-out from every
/// device, a password reset, a lock) is accepted again, so the process is not ready to serve
/// authentication even though it answers. The load is retried every 30 seconds; this check
/// turns healthy with the first success. The description is constant: never a count, a time or
/// an exception message, since <c>/ready</c> is anonymous.
/// </remarks>
public sealed class RevocationRehydrationHealthCheck : IHealthCheck
{
    internal const string LoadedDescription = "Token revocation list loaded.";
    internal const string NotLoadedDescription = "Token revocation list not loaded.";

    private readonly RevocationRehydrationStatus _status;

    public RevocationRehydrationHealthCheck(RevocationRehydrationStatus status)
    {
        _status = status;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_status.IsLoaded
            ? HealthCheckResult.Healthy(LoadedDescription)
            : new HealthCheckResult(context.Registration.FailureStatus, NotLoadedDescription));
    }
}
