using Auth.Domain.Constants;
using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// The bot check on the anonymous sign-in surfaces (S13). A password sign-in, a sign-up start, a
/// code start and a reset request carry a one-time token from Cloudflare Turnstile's widget, and
/// the server verifies it before any database work.
/// </summary>
/// <remarks>
/// Every refusal is a Validation error (400), never a 500: the request was refused, not broken.
/// The client shows the server's sentence for each code, which is published (ADR 0001).
/// </remarks>
public static class BotDefenseErrors
{
    /// <summary>The request carries no token while the check is enforced.</summary>
    public static Error TokenMissing => Error.Validation(
        code: "BotDefense.TokenMissing",
        description: "The request carries no bot-check token. Complete the check on the page, then try again.",
        metadata: new() { [ErrorMetadataKeys.Property] = "BotToken" });

    /// <summary>
    /// Cloudflare did not accept the token, or accepted one that was made for another page,
    /// another host or too long ago. A token is good for one request only.
    /// </summary>
    public static Error TokenRejected => Error.Validation(
        code: "BotDefense.TokenRejected",
        description: "The bot-check token was not accepted. Let the check on the page run again, then try again.",
        metadata: new() { [ErrorMetadataKeys.Property] = "BotToken" });

    /// <summary>
    /// The check cannot run (Cloudflare unreachable, or the server's keys not in place) and this
    /// request is not let through: a sign-up, or any request once the per-address allowance is
    /// spent, or every request while the fail mode is Closed.
    /// </summary>
    public static Error Unavailable => Error.Validation(
        code: "BotDefense.Unavailable",
        description: "The bot check cannot be completed right now. Try again in a few minutes.");

    /// <summary>
    /// One of Cloudflare's published testing keys was offered for Production, where it would let
    /// every request through.
    /// </summary>
    public static Error TestingKeyInProduction => Error.Validation(
        code: "BotDefense.TestingKeyInProduction",
        description: "Cloudflare's testing keys pass every check and cannot be used in Production. Use the keys of the deployment's own widget.",
        metadata: new() { [ErrorMetadataKeys.Property] = "Value" });
}
