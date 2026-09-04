using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Auth.Shared.Diagnostics;

/// <summary>
/// The line a refused request leaves behind.
/// </summary>
/// <remarks>
/// Both hosts throttle, and until this existed neither wrote anything when it
/// refused. A rejection left only the generic request-completed line with a 429
/// on it, and that line cannot say WHICH allowance ran out. The allowances differ
/// by an order of magnitude — twenty a minute for the interactive auth surface
/// against two hundred for registration, over a thousand for the edge's global
/// bucket — so "a 429 happened" is not enough to tell an attack from a launch, or
/// a limit set too low from one doing exactly its job.
/// <para>
/// That absence is not a cosmetic gap. Every limit in this system is a number an
/// operator is invited to change from the console, and none of them could be
/// evaluated after the change: a limit lowered onto real users produced silence,
/// and the only visible symptom was that sign-ups were slower than expected.
/// </para>
/// <para>
/// Shared between the two hosts on purpose. An operator reads these lines
/// interleaved, so a refusal at the edge and a refusal at the API have to be
/// comparable at a glance and searchable by one string.
/// </para>
/// </remarks>
public static class RateLimitRejectionLog
{
    /// <summary>
    /// The limiter policy the endpoint opted into, or <c>null</c> when nothing on
    /// the endpoint named one.
    /// </summary>
    /// <remarks>
    /// A null here means different things in the two hosts, and neither may
    /// assume the other's meaning: the API attaches every limit to an endpoint,
    /// so null is an anomaly worth seeing; the gateway also runs a limiter that
    /// belongs to no endpoint at all, so null there is ordinary and names it.
    /// </remarks>
    public static string? PolicyOf(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    /// <summary>
    /// Writes the refusal at warning level.
    /// </summary>
    /// <param name="logger">Where the line goes.</param>
    /// <param name="context">The refused request.</param>
    /// <param name="limiter">
    /// The allowance that ran out. This is the field an operator searches on, so
    /// it carries the policy name rather than a description of it.
    /// </param>
    /// <param name="clientId">
    /// The partition the allowance was drawn from. Every limiter in both hosts
    /// partitions by client address, so this says who was refused — and, read
    /// against <paramref name="limiter"/>, whether one caller is spending an
    /// allowance or a crowd is sharing one.
    /// </param>
    /// <param name="retryAfterSeconds">What the caller was told to wait.</param>
    public static void Write(
        ILogger logger,
        HttpContext context,
        string limiter,
        string? clientId,
        double retryAfterSeconds) =>
        logger.LogWarning(
            "Rate limit refused {Method} {Path}: limiter {Limiter}, client {ClientId}, retry after {RetryAfterSeconds}s",
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            limiter,
            clientId ?? "unknown",
            retryAfterSeconds);
}
