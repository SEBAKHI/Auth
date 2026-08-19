namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Decides whether an external ID token's <c>nonce</c> claim and the value the
/// caller presented amount to a valid pairing.
/// </summary>
/// <remarks>
/// <para>
/// Extracted so the rule can be tested. Both provider implementations reach it
/// only after a signature check that is a static call fetching live keys over
/// the network (<c>GoogleJsonWebSignature.ValidateAsync</c>, Apple's JWKS
/// fetch), which leaves no seam — so the comparison that follows had no
/// coverage of its own, and that is precisely where the defect lived.
/// </para>
/// <para>
/// <b>The rule is driven by the token, never by the caller.</b> Both providers
/// previously ran the comparison only when the caller supplied a nonce, which
/// meant a replayer holding a captured token could omit the field and have the
/// check skipped: the token then validated on signature, audience and expiry
/// alone, and the browser binding it was minted with was never examined. The
/// caller is the attacker in that scenario, so it must not be the party
/// deciding whether the check runs.
/// </para>
/// <para>
/// A token carrying no nonce claim, presented with no nonce, still passes —
/// so this cannot break a provider or client that never used one.
/// </para>
/// </remarks>
public static class ExternalNonceComparison
{
    /// <summary>
    /// True when neither side carries a nonce, or when both carry the same one.
    /// False whenever exactly one side has a value, which is the stripped-nonce
    /// replay this exists to refuse.
    /// </summary>
    public static bool IsSatisfied(string? tokenNonce, string? presentedNonce)
    {
        var tokenHasNonce = !string.IsNullOrEmpty(tokenNonce);
        var callerHasNonce = !string.IsNullOrEmpty(presentedNonce);

        if (!tokenHasNonce && !callerHasNonce)
        {
            return true;
        }

        if (tokenHasNonce != callerHasNonce)
        {
            return false;
        }

        return string.Equals(tokenNonce, presentedNonce, StringComparison.Ordinal);
    }
}
