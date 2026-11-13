namespace Auth.Application.Common.BotDefense;

/// <summary>What the bot check concluded about one request (BD5).</summary>
public enum BotDefenseVerdictKind
{
    /// <summary>Cloudflare accepted the token, for this surface and an accepted host, recently.</summary>
    Passed = 0,

    /// <summary>
    /// The token was refused: Cloudflare said no, or said yes for another surface, another host or
    /// too long ago, or it is the public testing token in Production. Final: never retried.
    /// </summary>
    Rejected = 1,

    /// <summary>The request carried no token.</summary>
    Missing = 2,

    /// <summary>Cloudflare could not be asked: transport failure, deadline, 5xx, 429, or the breaker open.</summary>
    Unavailable = 3,

    /// <summary>
    /// The server's own keys are wrong or missing (a secret Cloudflare refuses, a sitekey without a
    /// secret or the reverse, or a testing key in Production): nothing a visitor can fix.
    /// </summary>
    Misconfigured = 4,

    /// <summary>
    /// Neither the sitekey nor the secret is set: the check is not in place, and nothing is
    /// refused. The posture reports it.
    /// </summary>
    Unconfigured = 5,
}

/// <summary>One verdict, with what the verdict line records about it (BD12). Never the token or the secret.</summary>
/// <param name="Kind">The conclusion.</param>
/// <param name="Codes">Cloudflare's error codes, or the local reason (for example <c>action-mismatch</c>).</param>
/// <param name="Hostname">The host Cloudflare says the token was made on, when it answered.</param>
/// <param name="Attempts">How many calls were made (0, 1 or 2).</param>
/// <param name="ElapsedMs">How long the verification took, calls included.</param>
public sealed record BotDefenseVerdict(
    BotDefenseVerdictKind Kind,
    IReadOnlyList<string> Codes,
    string? Hostname = null,
    int Attempts = 0,
    long ElapsedMs = 0)
{
    /// <summary>A verdict reached without calling Cloudflare.</summary>
    public static BotDefenseVerdict Local(BotDefenseVerdictKind kind, params string[] codes) => new(kind, codes);
}
