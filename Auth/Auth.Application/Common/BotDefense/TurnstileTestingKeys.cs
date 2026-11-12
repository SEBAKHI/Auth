using System.Text.RegularExpressions;

namespace Auth.Application.Common.BotDefense;

/// <summary>
/// Cloudflare's public Turnstile testing keys. They answer by rule, not by checking anybody: a
/// testing secret accepts the dummy token every testing sitekey issues, from any host. Useful in
/// development and tests; in Production one of them would let every request through, so a
/// testing key there is a misconfiguration (BD5, BD8).
/// </summary>
/// <remarks>
/// Recognised by their documented shape, a digit, <c>x</c>, zeros and two capital letters
/// (<c>1x00000000000000000000AA</c>), rather than by a list alone, so a testing key Cloudflare
/// adds later is recognised too. Real keys begin <c>0x4</c> and carry other characters.
/// </remarks>
public static partial class TurnstileTestingKeys
{
    /// <summary>The token every testing sitekey issues, and every testing secret accepts.</summary>
    public const string DummyToken = "XXXX.DUMMY.TOKEN.XXXX";

    /// <summary>The documented testing sitekeys.</summary>
    public static readonly IReadOnlyList<string> Sitekeys =
    [
        "1x00000000000000000000AA", // always passes, visible
        "2x00000000000000000000AB", // always blocks, visible
        "1x00000000000000000000BB", // always passes, invisible
        "2x00000000000000000000BB", // always blocks, invisible
        "3x00000000000000000000FF", // forces an interactive challenge
    ];

    /// <summary>The documented testing secrets.</summary>
    public static readonly IReadOnlyList<string> Secrets =
    [
        "1x0000000000000000000000000000000AA", // always passes
        "2x0000000000000000000000000000000AA", // always fails
        "3x0000000000000000000000000000000AA", // answers "token already spent"
    ];

    /// <summary>Whether <paramref name="key"/> (a sitekey or a secret) is one of Cloudflare's testing keys.</summary>
    public static bool IsTestingKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && TestingKeyShape().IsMatch(key.Trim());

    [GeneratedRegex("^[1-9]x0+[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex TestingKeyShape();
}
