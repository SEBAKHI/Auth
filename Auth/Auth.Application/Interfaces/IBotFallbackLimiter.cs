namespace Auth.Application.Interfaces;

/// <summary>
/// The per-address allowance that lets a sign-in, a code start or a reset request through while
/// the bot check cannot run (S13, BD6, rate-limit name <c>bot-fallback</c>):
/// <c>RateLimiting:BotFallbackPermitLimit</c> requests per <c>RateLimiting:BotFallbackWindowSeconds</c>,
/// 5 per 300 seconds by default.
/// </summary>
/// <remarks>
/// Counted in memory, per process, keyed like every other per-address limit (the settings
/// version, then the address): a saved change opens fresh windows. Rate shaping, not a security
/// boundary (RL8): the account lock and the per-address limits stay in force behind it.
/// </remarks>
public interface IBotFallbackLimiter
{
    /// <summary>Takes one permit for <paramref name="clientIp"/>; false when its window is spent.</summary>
    bool TryAcquire(string? clientIp);
}
