namespace Auth.Application.Common.BotDefense;

/// <summary>
/// A request on an anonymous surface that carries a bot-check token (S13, BD1, BD3): a password
/// sign-in, a sign-up start, a code start and a reset request. <see cref="Behaviors.BotDefenseBehavior{TRequest,TResponse}"/>
/// verifies the token before the handler runs, so before any database work.
/// </summary>
/// <remarks>
/// Exactly four commands carry this marker; a guard test keeps the list as it is. The endpoints
/// a signed-in person calls (an administrator's "send reset link", a holder's "set a password")
/// do not: their caller is already authenticated.
/// </remarks>
public interface IBotProtectedRequest
{
    /// <summary>The token the page's widget produced, or null when the request carried none.</summary>
    string? BotToken { get; }

    /// <summary>The surface's action (<see cref="BotDefenseActions"/>), which Cloudflare's answer must name.</summary>
    string Action { get; }

    /// <summary>
    /// The caller's address, for the per-address allowance alone. It is never sent to Cloudflare
    /// (IP10): the verification sends the secret, the token and an idempotency key, nothing else.
    /// </summary>
    string? ClientIp { get; }
}
