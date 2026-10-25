using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS1: a keyring that never rotated issues, byte for byte, what the platform
/// stored before it existed — for every purpose, the two labelled ones included.
/// If one value moved, every user would be signed out and every emailed link and
/// code would die at the deploy (E10).
/// </summary>
/// <remarks>
/// The expected values are literals captured on 2026-10-21 from the classes this
/// keyring replaced (RefreshTokenKeyService.ComputeTokenHash and
/// WebhookKeyHasher.ComputeHash at 01733635), over key bytes 0..31, through the
/// same callers (HmacOtpHasher, PendingRegistrationHandle, StepUpTicketService).
/// They are not recomputed here: a recomputation would agree with whatever the
/// code does today.
/// </remarks>
public class LegacyDerivationCompatibilityTests
{
    private static readonly Guid UserId = Guid.Parse("3f2c1b7a-9d4e-4c51-8a2b-6e7f0a1b2c3d");

    /// <summary>Purpose, the value its caller passes, and the value stored before S11.</summary>
    public static TheoryData<CredentialPurpose, string, string> PlainGoldens => new()
    {
        { CredentialPurpose.RefreshToken, "refresh-token-sample", "yx+VODanPp6VpyC7H2hUkv9Ow+KBCAunnnoN+ICswtM=" },
        { CredentialPurpose.IdpSession, "idp-session-sample", "Z24vB0Cy+OmG0sC0Q2aL65PAGZHrw90di5bh0CAfsE4=" },
        { CredentialPurpose.AuthorizationCode, "authorization-code-sample", "fSPvEBMFlmo32yNf+MuL72eP6/OlXdUeev6t2hyLVMs=" },
        { CredentialPurpose.AuthorizationRequestHandle, "authorization-request-handle-sample", "ax/mchmn97Tv7M5uzR9DFgv3j+EYUaH2IeuUWBXmLfQ=" },
        { CredentialPurpose.PasswordReset, "password-reset-sample", "vLP+zD9XCint06ZW2hILo/IlsPyW1l8gVOCVfdnf4UE=" },
        { CredentialPurpose.Invitation, "invitation-sample", "O8De9fQaFPTaFocsEWoDjHPLQcjIgdpnLXPfxERL2TM=" },
        { CredentialPurpose.TwoFactorChallenge, "two-factor-challenge-sample", "Z8/h3PSWFnTE7FdHnNObBgKaorbd9NexwzvUP7Ol30s=" },
        { CredentialPurpose.ExternalNonce, "external-nonce-sample", "QWx/0p73Trfmuyuljj8/MRgrIyEliJdieXUiINLxsJ0=" },
        { CredentialPurpose.WebhookKey, "wk_live_sample", "S2HEKqunY1YOsJmIBLV8BjHWXWzdVkpq/dDghqx3M20=" },
    };

    private const string StepUpTicketGolden = "1792584000|client-1|QHuRdyGNxH/N3LxsGXXJL7JeZJl5WjhOrnFaM9ylpXU=";
    private const string OtpGolden = "XkzYsf7oIJP4Ehrkv4QD+gI4iSDVxZPnDGHTiTX/LI8=";
    private const string TwoFactorBindGolden = "3SC0ENNe2JbzEvzeSUdBxzaC7E6yjlv+pHNc6pNwdqg=";
    private const string PendingRegistrationHandleGolden = "zcaaF3nkkYBcdefkUlROocgMg0mYfoKPqSvcIIqWL9Q=";

    private static readonly DateTime StepUpDemandedAt = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string> Rings => new() { "single key", "cutover recorded while key 1 is current" };

    /// <summary>
    /// The two rings that issue the legacy form: never rotated, and the RV-P4-3 case —
    /// a cutover recorded while key 1 is still current must NOT start deriving from key 1.
    /// </summary>
    private static KeyringCredentialHasher HasherFor(string ring) => ring switch
    {
        "single key" => TestCredentialHashers.Over(KeyringTestRings.SingleKey()),
        _ => TestCredentialHashers.Over(HmacKeyring.Create(
            [new HmacKey(1, KeyringTestRings.Key1Material)],
            currentKeyId: 1, nextKeyId: null, legacyCutoverUtc: KeyringTestRings.Cutover, legacyVerifyUntilUtc: null).Value)
    };

    [Theory]
    [MemberData(nameof(PlainGoldens))]
    public void Hash_UnrotatedRing_IssuesTodaysBytes(CredentialPurpose purpose, string value, string golden)
    {
        foreach (var ring in new[] { "single key", "cutover recorded while key 1 is current" })
        {
            var hasher = HasherFor(ring);

            hasher.Hash(purpose, value).Should().Be(golden, ring);
            hasher.Candidates(purpose, value).Values.Should().Equal([golden], ring);
            hasher.Verify(purpose, value, golden).Should().BeTrue(ring);
        }
    }

    [Theory]
    [MemberData(nameof(Rings))]
    public void TheLabelledCallers_IssueTodaysBytes(string ring)
    {
        var hasher = HasherFor(ring);
        var otp = new HmacOtpHasher(hasher, new Mock<IPasswordHasher>().Object);

        new StepUpTicketService(hasher).Issue("client-1", "challenge-1", StepUpDemandedAt).Should().Be(StepUpTicketGolden);
        otp.Hash(UserId.ToString(), "123456").Should().Be(OtpGolden);
        otp.Hash(FirstFactorEmailProof.HashScope(UserId), "654321").Should().Be(TwoFactorBindGolden);
        new PendingRegistrationHandle(hasher).For("person@example.com").Should().Be(PendingRegistrationHandleGolden);

        // And what was stored before the deploy still verifies after it.
        otp.Verify(UserId.ToString(), "123456", OtpGolden).Should().BeTrue();
        otp.Verify(FirstFactorEmailProof.HashScope(UserId), "654321", TwoFactorBindGolden).Should().BeTrue();
        new StepUpTicketService(hasher)
            .TryValidate(StepUpTicketGolden, "client-1", "challenge-1", StepUpDemandedAt.AddMinutes(1), TimeSpan.FromMinutes(30), out _)
            .Should().BeTrue();
    }

    [Fact]
    public void TheOtpScopes_RouteToTheirOwnPurposes()
    {
        // Two purposes over one message format: the split changes no stored byte (above),
        // and a bind code is a credential of its own from the first rotation on.
        HmacOtpHasher.PurposeOf(FirstFactorEmailProof.HashScope(UserId)).Should().Be(CredentialPurpose.TwoFactorBind);
        // The emailed second factor's code (S24): a credential of its own, never an OTP.
        HmacOtpHasher.PurposeOf(EmailCodeProofStrategy.HashScope(UserId)).Should().Be(CredentialPurpose.TwoFactorEmailCode);
        HmacOtpHasher.PurposeOf(UserId.ToString()).Should().Be(CredentialPurpose.Otp);
        HmacOtpHasher.PurposeOf("pending-registration:" + UserId).Should().Be(CredentialPurpose.Otp);
    }

    [Theory]
    [MemberData(nameof(PlainGoldens))]
    public void Candidates_AfterTheCutover_StillFindTodaysValue_Last(CredentialPurpose purpose, string value, string golden)
    {
        // The first promote: new values are subkey fingerprints of key 2, and a credential
        // stored before it is still found under the legacy form, inside its window.
        var hasher = TestCredentialHashers.Over(KeyringTestRings.Rotated(), new FixedTimeProvider(KeyringTestRings.Cutover.AddMinutes(1)));

        var candidates = hasher.Candidates(purpose, value).Values;

        candidates.Should().HaveCount(2);
        candidates[^1].Should().Be(golden, "the legacy form comes last");
        candidates[0].Should().NotBe(golden).And.Be(hasher.Hash(purpose, value), "the issuing key comes first");
    }
}
