using Auth.Infrastructure.Authentication;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The external ID token's nonce binding must be checked because the TOKEN
/// carries one, never because the caller chose to mention it.
/// </summary>
/// <remarks>
/// Both providers used to compare the values only when the caller supplied a
/// nonce. The caller is the attacker in a replay: someone holding a captured
/// Google or Apple ID token could simply omit the field, and the token then
/// validated on signature, audience and expiry alone while the browser binding
/// it was minted with went unexamined. The rollout switch
/// <c>ExternalAuth:RequireNonce</c> ships off, so both layers were open at
/// once.
///
/// The comparison lives behind a static signature check that fetches live keys
/// over the network, so it had no seam and no coverage. Extracting it is what
/// makes these cases expressible.
/// </remarks>
public class ExternalNonceComparisonTests
{
    [Fact]
    public void TokenCarriesNonce_CallerOmitsIt_IsRefused()
    {
        // This is the replay. It used to pass.
        ExternalNonceComparison.IsSatisfied("browser-bound-value", null)
            .Should().BeFalse("stripping the field must not skip the check");
    }

    [Fact]
    public void TokenCarriesNonce_CallerSendsEmpty_IsRefused()
    {
        ExternalNonceComparison.IsSatisfied("browser-bound-value", "")
            .Should().BeFalse("an empty string is an omission by another spelling");
    }

    [Fact]
    public void MatchingPair_IsAccepted()
    {
        // The first-party path: the SPA fetches a nonce, sends it to the
        // provider when minting the token AND to this API alongside it.
        ExternalNonceComparison.IsSatisfied("browser-bound-value", "browser-bound-value")
            .Should().BeTrue();
    }

    [Fact]
    public void MismatchedPair_IsRefused()
    {
        ExternalNonceComparison.IsSatisfied("browser-bound-value", "someone-elses-value")
            .Should().BeFalse();
    }

    [Fact]
    public void DifferingOnlyInCase_IsRefused()
    {
        // The nonce is an opaque binding, not a name; a case-insensitive
        // comparison would widen the space an attacker has to hit.
        ExternalNonceComparison.IsSatisfied("AbC123", "abc123")
            .Should().BeFalse();
    }

    [Fact]
    public void CallerSendsNonce_TokenCarriesNone_IsRefused()
    {
        // Presenting a value the token was never minted with is either a
        // confused client or a substituted token; neither should proceed.
        ExternalNonceComparison.IsSatisfied(null, "browser-bound-value")
            .Should().BeFalse();
    }

    [Fact]
    public void NeitherSideCarriesANonce_IsAccepted()
    {
        // Backward compatibility, and the reason this tightening is safe to
        // ship before the rollout switch is flipped: a flow that never used a
        // nonce is untouched.
        ExternalNonceComparison.IsSatisfied(null, null).Should().BeTrue();
    }
}
