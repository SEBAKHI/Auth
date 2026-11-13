using Auth.Application.Common.BotDefense;

namespace Auth.Application.Interfaces;

/// <summary>
/// Verifies a bot-check token for one surface (S13, BD5). A Strategy: Turnstile is the
/// implementation, in Infrastructure.
/// </summary>
/// <remarks>
/// The configuration is judged before the token: with neither key set the verdict is
/// <see cref="BotDefenseVerdictKind.Unconfigured"/> whatever the request carried; with one key
/// missing, or a testing key in Production, <see cref="BotDefenseVerdictKind.Misconfigured"/>,
/// and nothing is called. Never throws for an outage: a call that fails is
/// <see cref="BotDefenseVerdictKind.Unavailable"/>.
/// </remarks>
public interface IBotDefenseVerifier
{
    /// <param name="token">The token the request carried, or null.</param>
    /// <param name="action">The surface's action (<see cref="BotDefenseActions"/>).</param>
    /// <param name="cancellationToken">The request's token.</param>
    Task<BotDefenseVerdict> VerifyAsync(string? token, string action, CancellationToken cancellationToken);
}
