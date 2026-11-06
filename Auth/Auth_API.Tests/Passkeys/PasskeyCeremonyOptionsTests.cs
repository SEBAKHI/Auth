using System.Buffers.Text;
using System.Text.Json;
using Auth.Application.Features.Passkeys.BeginPasskeyAssertion;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B (PK5, PK6, T-B2): the options of a passkey sign-in, and when a page that asks again
/// receives the same ones. No credential is named — the device offers the discoverable passkeys
/// it holds for the RP ID — user verification is required, the challenge is 32 random bytes held
/// five minutes, and the browser holds only a handle whose keyed fingerprint is stored.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>userVerification</c> "preferred" in the adapter; the
/// re-issue read skipped (a new row on every page load); the repeated-cookie case offered the live
/// challenge again. «Re-issue on CreatedAt» is a statement break, pinned by the store's SQL text
/// (<c>PasskeyStoreSqlTextTests</c>).
/// </remarks>
public class PasskeyCeremonyOptionsTests
{
    [Fact]
    public async Task TheOptions_NameNoCredential_RequireUserVerification_AndCarryAFreshChallenge()
    {
        using var fixture = new PasskeySignInFixture();

        using var options = JsonDocument.Parse(await fixture.OptionsAsync());
        var root = options.RootElement;

        root.GetProperty("allowCredentials").GetArrayLength().Should().Be(0, "PK5: the device offers what it holds");
        root.GetProperty("userVerification").GetString().Should().Be("required", "PK5: a passkey proves two factors only with UV");
        root.GetProperty("rpId").GetString().Should().Be(PasskeyTestDoubles.RpId);
        root.GetProperty("timeout").GetInt64().Should().Be(300_000);
        Base64Url.DecodeFromChars(root.GetProperty("challenge").GetString()).Should().HaveCount(32);
    }

    [Fact]
    public async Task ANewChallenge_StoresTheHandlesFingerprint_NamesNoAccount_AndExpiresInFiveMinutes()
    {
        using var fixture = new PasskeySignInFixture();
        WebAuthnChallenge? stored = null;
        fixture.Challenges.Setup(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<WebAuthnChallenge, CancellationToken>((challenge, _) => stored = challenge)
            .Returns(Task.CompletedTask);

        var started = await fixture.Begin().Handle(new BeginPasskeyAssertionCommand(null, false), CancellationToken.None);

        started.IsError.Should().BeFalse();
        started.Value.ChallengeHandle.Should().Be(PasskeySignInFixture.Handle, "a new challenge, a new cookie");
        stored.Should().NotBeNull();
        stored!.Purpose.Should().Be(WebAuthnChallengePurpose.Assertion);
        stored.UserId.Should().BeNull("a sign-in names no account until the passkey does");
        stored.HandleHash.Should().Be(new FakeCredentialHasher().Of(PasskeySignInFixture.Handle)).And.NotBe(PasskeySignInFixture.Handle);
        stored.OptionsJson.Should().Be(started.Value.OptionsJson);
        stored.ExpiresAt.Should().Be(stored.CreatedAt + WebAuthnChallenge.Lifetime);
        started.Value.ExpiresAt.Should().Be(stored.ExpiresAt);
    }

    [Fact]
    public async Task OneCookie_WhoseChallengeHasMoreThanAMinuteLeft_GetsTheSameOptions_WithNoNewRow_AndTheCookieAsItIs()
    {
        using var fixture = new PasskeySignInFixture();
        var live = WebAuthnChallenge.IssueForAssertion("fp-live", "{\"challenge\":\"bGl2ZQ\"}", DateTime.UtcNow.AddMinutes(-1));
        fixture.Challenges.Setup(c => c.GetReusableAssertionAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);

        var started = await fixture.Begin().Handle(new BeginPasskeyAssertionCommand("live-handle", false), CancellationToken.None);

        started.Value.OptionsJson.Should().Be(live.OptionsJson);
        started.Value.ExpiresAt.Should().Be(live.ExpiresAt);
        started.Value.ChallengeHandle.Should().BeNull("the cookie keeps naming the challenge it names");
        fixture.Challenges.Verify(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Challenges.Verify(c => c.GetReusableAssertionAsync(
            It.Is<CredentialHashCandidates>(candidates => candidates.ValuesFor(CredentialPurpose.WebAuthnChallenge).Contains(new FakeCredentialHasher().Of("live-handle"))),
            It.IsAny<CancellationToken>()), Times.Once, "found by every accepted fingerprint of the handle");
    }

    [Fact]
    public async Task OneCookie_WhoseChallengeHasAMinuteOrLessLeft_OrIsSpent_GetsANewOne()
    {
        // The store finds none: it reads only challenges with more than a minute left (T-B2).
        using var fixture = new PasskeySignInFixture();
        fixture.Challenges.Setup(c => c.GetReusableAssertionAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebAuthnChallenge?)null);

        var started = await fixture.Begin().Handle(new BeginPasskeyAssertionCommand("old-handle", false), CancellationToken.None);

        started.Value.ChallengeHandle.Should().Be(PasskeySignInFixture.Handle);
        fixture.Challenges.Verify(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARepeatedCookie_GetsANewChallenge_AndIsNeverOfferedTheOneItNames()
    {
        using var fixture = new PasskeySignInFixture();

        var started = await fixture.Begin().Handle(new BeginPasskeyAssertionCommand(null, ChallengeHandleRepeated: true), CancellationToken.None);

        started.Value.ChallengeHandle.Should().Be(PasskeySignInFixture.Handle);
        fixture.Challenges.Verify(c => c.GetReusableAssertionAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()), Times.Never,
            "one of two cookies may be a plant: neither is trusted to name the challenge");
        fixture.Challenges.Verify(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhilePasskeysDoNotRun_NoOptionsAreOffered_AndNothingIsWritten()
    {
        using var fixture = new PasskeySignInFixture { Feature = PasskeyTestDoubles.Feature(enabled: false) };

        var started = await fixture.Begin().Handle(new BeginPasskeyAssertionCommand(null, false), CancellationToken.None);

        started.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.Challenges.Verify(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheReuseMargin_IsTheMinuteTheStoreReads()
    {
        WebAuthnChallenge.ReuseMargin.Should().Be(TimeSpan.FromSeconds(60));
        Auth.Infrastructure.Persistence.WebAuthnChallengeRepository.ReusableAssertionSql
            .Should().Contain($"DATEADD(SECOND, {WebAuthnChallenge.ReuseMargin.TotalSeconds:0}, SYSUTCDATETIME())");
    }

    [Fact]
    public void TheStartedSignIn_NeverPrintsItsHandle()
    {
        new PasskeyAssertionStarted("{}", "secret-handle", DateTime.UtcNow).ToString().Should().NotContain("secret-handle");
        new BeginPasskeyAssertionCommand("secret-handle", false).ToString().Should().NotContain("secret-handle");
    }
}
