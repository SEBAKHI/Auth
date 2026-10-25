using System.Text.RegularExpressions;

namespace Auth.Sdk.Authorization;

/// <summary>
/// The SDK's one permission matcher, used by both authorization handlers. It applies the
/// rule the AuthSystem API applies (<c>PermissionCode.Matches</c>), which this package
/// cannot reference: a held code matches itself (case-insensitively); a well-formed
/// <c>*</c> matches everything; a well-formed <c>prefix:*</c> matches the prefix and every
/// code under it. A held code outside the format (<c>*:*</c>, <c>users*</c>, <c>a:*:b</c>)
/// matches only itself: expanded as a wildcard, <c>*:*</c> would cover the global <c>*</c>.
/// </summary>
internal static partial class PermissionMatcher
{
    /// <summary>
    /// The permission-code format, the same pattern as the API's <c>PermissionCode.Pattern</c>:
    /// <c>*</c>, or segments of <c>[a-z0-9_-]</c> joined by <c>:</c>, optionally ending in
    /// <c>:*</c>, up to <c>\z</c>. The parity test in the API's test project holds the two
    /// together.
    /// </summary>
    internal const string Pattern = @"^(\*|[a-z0-9_\-]+(:[a-z0-9_\-]+)*(:\*)?)\z";

    /// <summary>Whether any of <paramref name="held"/> covers <paramref name="required"/>.</summary>
    internal static bool MatchesAny(IEnumerable<string> held, string required) =>
        held.Any(code => Matches(code, required));

    /// <summary>Whether <paramref name="held"/> covers <paramref name="required"/>.</summary>
    internal static bool Matches(string held, string required)
    {
        if (string.Equals(held, required, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!CodeRegex().IsMatch(held.ToLowerInvariant()))
            return false;

        if (held == "*")
            return true;

        if (held.EndsWith(":*", StringComparison.Ordinal))
        {
            var prefix = held[..^2];
            return required.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)
                || string.Equals(required, prefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    [GeneratedRegex(Pattern)]
    private static partial Regex CodeRegex();
}
