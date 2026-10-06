using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="ExternalNonceGuard"/> — the rule that a provider
/// sign-in's nonce must be one this server issued to this browser.
/// </summary>
/// <remarks>
/// The property under test is narrow and worth stating exactly: the guard does
/// not check the token. It establishes only that the presented value came from
/// this server and reached this browser. Comparing it to the token's own claim
/// is the provider's job, and neither check is worth anything without the other
/// — which is what made the previous arrangement hollow, since a caller supplied
/// both sides of the only comparison being made.
/// </remarks>
public class ExternalNonceGuardTests
{
    private static ExternalNonceGuard Guard(bool requireNonce)
        => TestHelpers.CreateExternalNonceGuard(requireNonce);

    // The stand-in hasher in TestHelpers.
    private static string CookieFor(string nonce) => $"hash:{nonce}";

    [Fact]
    public void Validate_MatchingPair_Succeeds()
    {
        var result = Guard(requireNonce: true).Validate("nonce-abc", CookieFor("nonce-abc"));

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public void Validate_NonceFromAnotherBrowser_IsRejected()
    {
        // The replay this exists to stop. The attacker holds a token minted for
        // the victim's browser, so its nonce is the victim's; the cookie in the
        // attacker's browser vouches for a different one. Sending either value
        // fails one of the two comparisons.
        var result = Guard(requireNonce: true).Validate("victims-nonce", CookieFor("attackers-nonce"));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("ExternalAuth.NonceRequired");
    }

    [Theory]
    [InlineData(null, "hash:something")]
    [InlineData("", "hash:something")]
    [InlineData("nonce-abc", null)]
    [InlineData("nonce-abc", "")]
    public void Validate_MissingEitherHalf_IsRejected(string? nonce, string? cookie)
    {
        // One half alone proves nothing, so neither is optional once enforcement
        // is on. A caller who simply omits the cookie must not be waved through.
        var result = Guard(requireNonce: true).Validate(nonce, cookie);

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenEnforcementOff_AcceptsAnything()
    {
        // The rollback position. An older app sends a self-generated value backed
        // by no cookie, and that must still sign in while the switch is off.
        var guard = Guard(requireNonce: false);

        guard.Validate(null, null).IsError.Should().BeFalse();
        guard.Validate("locally-invented", null).IsError.Should().BeFalse();
        guard.Validate("mismatched", CookieFor("other")).IsError.Should().BeFalse();
    }

    /// <summary>
    /// OI-78: enforcement is the shipped default, so a fresh deployment with no
    /// override refuses a browser-generated nonce. The guard runs before the
    /// provider is consulted, for Google and Apple alike, on sign-in and on
    /// pending-deletion recovery.
    /// </summary>
    [Fact]
    public void Validate_WithShippedDefaults_RefusesAnUnissuedNonce_AndAcceptsAnIssuedOne()
    {
        var keyService = new Mock<IRefreshTokenKeyService>();
        keyService.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns((string value) => $"hash:{value}");
        var guard = new ExternalNonceGuard(keyService.Object, TestHelpers.CreateOptions(new ExternalAuthSettings()));

        guard.Validate(null, null).IsError.Should().BeTrue("no nonce at all");
        guard.Validate("locally-invented", null).IsError.Should().BeTrue("a browser-generated nonce no cookie backs");
        guard.Validate("nonce-abc", CookieFor("nonce-abc")).IsError.Should().BeFalse("the server-issued nonce");
    }

    [Fact]
    public void Validate_RejectionSaysNothingAboutWhichHalfFailed()
    {
        // Absent, mismatched and never-issued all answer identically, so probing
        // cannot distinguish "this browser holds no cookie" from "that nonce is
        // not the one it holds".
        var guard = Guard(requireNonce: true);

        var missing = guard.Validate(null, null);
        var mismatched = guard.Validate("nonce-abc", CookieFor("different"));

        missing.FirstError.Code.Should().Be(mismatched.FirstError.Code);
        missing.FirstError.Description.Should().Be(mismatched.FirstError.Description);
    }
}
