using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.VerifyPasskeyAssertion;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B (PK8, PK9, FA4 (4-4), FA5 (5-1)): signing in with a passkey, one case per step of the
/// handler, with the real WebAuthn library checking a software authenticator's answers. The
/// challenge is spent before anything is checked, so every refusal has spent it; the passkey is
/// found by its exact bytes; the user handle is compared here; the counter is judged here; the
/// record is conditional; a success is one session that proves two factors and lifts no lock.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the user-handle comparison dropped (another account's
/// handle signs in); <c>passwordProven: true</c> (the success row); the conditional record ignored
/// (the zero-row case signs in). PK9's <c>&amp;&amp;</c> break is pinned by
/// <c>UserPasskeyTests</c> and the accepting-verifier row here.
/// </remarks>
public class VerifyPasskeyAssertionCommandHandlerTests
{
    // ── Success ──

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AVerifiedPasskey_SignsIn_Once_ProvingTwoFactors_LiftingNoLock(bool staySignedIn)
    {
        using var fixture = new PasskeySignInFixture();

        var result = await fixture.SignInAsync(staySignedIn: staySignedIn);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        fixture.VerifySession(persistent: staySignedIn);
        fixture.Events.Verify(e => e.DispatchEventsAsync(fixture.Owner, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Passkeys.Verify(p => p.TryRecordAssertionAsync(
            It.Is<UserPasskey>(passkey => passkey.Id == fixture.Passkey.Id && passkey.SignCount == 1 && passkey.LastUsedAt != null),
            0, CancellationToken.None), Times.Once, "the counter checked was 0; the write does not stop on a disconnect");
        fixture.ConsumeCalls.Should().Be(1);
    }

    [Fact]
    public async Task ASuccess_RaisesTheSignInEvent_WithThePasskeysMethods()
    {
        using var fixture = new PasskeySignInFixture();
        IReadOnlyList<Auth.Domain.Primitives.IDomainEvent>? raised = null;
        fixture.Events.Setup(e => e.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Callback<Auth.Domain.Primitives.AggregateRoot, CancellationToken>((root, _) => raised = root.DomainEvents.ToList())
            .Returns(Task.CompletedTask);

        (await fixture.SignInAsync()).IsError.Should().BeFalse();

        raised.Should().ContainSingle().Which.Should().BeOfType<UserLoggedInEvent>()
            .Which.Methods.Should().Be(AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified));
    }

    [Fact]
    public void NoSecondStep_AndNoSignInRow_CanBeOpenedByThisHandler()
    {
        // M3: a passkey the device verified the person for proves two factors, so no TOTP
        // challenge exists to open; FA4 (4-4): no failure row exists to write.
        var parameters = typeof(VerifyPasskeyAssertionCommandHandler).GetConstructors().Single().GetParameters()
            .Concat(typeof(PasskeyAssertionCeremony).GetConstructors().Single().GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        parameters.Should().NotBeEmpty();
        parameters.Should().NotContain(typeof(ITwoFactorChallengeService))
            .And.NotContain(typeof(ILoginAttemptRepository))
            .And.NotContain(typeof(IAccountLockout), "nothing here lifts or counts a lock");
    }

    // ── 1. The cookie ──

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(null, true)]
    public async Task NoCookie_OrItsNameSentTwice_IsRefused_AndSpendsNothing(string? handle, bool repeated)
    {
        using var fixture = new PasskeySignInFixture();
        var response = await fixture.AnswerAsync();

        var result = await fixture.Verify().Handle(fixture.VerifyCommand(response, handle, repeated), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.ConsumeCalls.Should().Be(0, "ARR-0687: one of two cookies may be a plant");
        fixture.VerifyNoSession();
    }

    // ── 2-7. Every refusal after the consume has spent the challenge ──

    public static TheoryData<string> ClientFaults => new()
    {
        "a corrupt signature",
        "no user verification",
        "another challenge",
        "the apex origin",
        "a sibling origin",
        "a cross-origin frame",
        "no user handle",
        "another account's user handle",
        "a raw id that does not read",
        "no passkey with the id",
        "a removed passkey",
        "another RP ID",
        "a response that is not JSON",
    };

    [Theory]
    [MemberData(nameof(ClientFaults))]
    public async Task EachClientFault_IsVerificationFailed_WithTheChallengeSpent_AndNoSession(string fault)
    {
        using var fixture = new PasskeySignInFixture();
        var options = await fixture.OptionsAsync();
        string Answer(CeremonyTweaks? tweaks = null, string origin = PasskeySignInFixture.Origin, byte[]? handle = null) =>
            fixture.Authenticator.Assert(options, origin, handle ?? PasskeySignInFixture.OwnerHandle, tweaks);

        var response = fault switch
        {
            "a corrupt signature" => Answer(new CeremonyTweaks { CorruptSignature = true }),
            "no user verification" => Answer(new CeremonyTweaks { UserVerified = false }),
            "another challenge" => Answer(new CeremonyTweaks { Challenge = SoftwareAuthenticator.Base64Url(new byte[32]) }),
            "the apex origin" => Answer(origin: "https://example.com"),
            "a sibling origin" => Answer(origin: "https://evil.example.com"),
            "a cross-origin frame" => Answer(new CeremonyTweaks { CrossOrigin = true, TopOrigin = "https://example.com" }),
            "no user handle" => WithoutUserHandle(Answer()),
            "another account's user handle" => Answer(handle: Enumerable.Repeat((byte)9, 64).ToArray()),
            "a raw id that does not read" => WithRawId(Answer(), "%%%"),
            "no passkey with the id" => Answer(new CeremonyTweaks { ResponseCredentialId = new byte[32] }),
            "a removed passkey" => RemovedAnd(fixture, Answer()),
            "another RP ID" => Answer(new CeremonyTweaks { RpId = "example.org" }),
            "a response that is not JSON" => "not json",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        var result = await fixture.Verify().Handle(fixture.VerifyCommand(response), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, fault);
        fixture.ConsumeCalls.Should().Be(1, $"{fault}: the challenge was spent before the check");
        fixture.VerifyNoSession();
        fixture.Passkeys.Verify(p => p.TryRecordAssertionAsync(It.IsAny<UserPasskey>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never, fault);
        fixture.CeremonyLog.Entries.Should().Contain(entry => entry.Values.Contains(PasskeySignalCodes.VerificationFailed), fault);

        // The same answer again finds no challenge to spend.
        (await fixture.Verify().Handle(fixture.VerifyCommand(response), CancellationToken.None))
            .FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, $"{fault}, replayed");
    }

    [Fact]
    public async Task ASpentChallenge_SignsNoOneIn_TheSecondTime()
    {
        using var fixture = new PasskeySignInFixture();
        var response = await fixture.AnswerAsync();

        (await fixture.Verify().Handle(fixture.VerifyCommand(response), CancellationToken.None)).IsError.Should().BeFalse();
        var replay = await fixture.Verify().Handle(fixture.VerifyCommand(response), CancellationToken.None);

        replay.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.Builder.Invocations.Should().ContainSingle(invocation => invocation.Method.Name == nameof(ILoginResponseBuilder.BuildAsync));
    }

    /// <summary>P7-R15: two credential ids that differ only in letter case are two passkeys.</summary>
    [Fact]
    public async Task TwoCredentialIds_ThatDifferOnlyInLetterCase_AreTwoPasskeys()
    {
        var id = Encoding.ASCII.GetBytes("AbCdEfGhIjKlMnOpQrStUvWxYzAbCdEf");
        var swapped = Encoding.ASCII.GetBytes("aBcDeFgHiJkLmNoPqRsTuVwXyZaBcDeF");
        using var fixture = new PasskeySignInFixture(credentialId: id);

        var result = await fixture.Verify().Handle(
            fixture.VerifyCommand(await fixture.AnswerAsync(new CeremonyTweaks { ResponseCredentialId = swapped })), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.Passkeys.Verify(p => p.GetLiveByCredentialIdAsync(
            It.Is<byte[]>(bytes => bytes.SequenceEqual(swapped)), PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()), Times.Once,
            "the response's own bytes are looked up, never a case-folded text");
        fixture.VerifyNoSession();
    }

    [Fact]
    public async Task AStoredKeyThatCannotBeRead_IsAServerFault_AfterTheChallengeWasSpent()
    {
        using var fixture = new PasskeySignInFixture(storedPublicKey: [0xFF, 0x00]);

        var act = async () => await fixture.SignInAsync();

        await act.Should().ThrowAsync<InvalidOperationException>("N4: the error contract answers 500 Http.Unexpected, never the client's code");
        fixture.ConsumeCalls.Should().Be(1);
        fixture.VerifyNoSession();
    }

    // ── 7. The counter (PK9, T-B3) and the record (T-B4) ──

    [Fact]
    public async Task TwoZeroCounters_SignIn()
    {
        using var fixture = new PasskeySignInFixture();

        (await fixture.SignInAsync(new CeremonyTweaks { SignCount = 0 })).IsError.Should().BeFalse("an authenticator that does not count");
        fixture.Passkeys.Verify(p => p.SealCounterRegressionAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(0u)]
    public async Task ACounterThatDidNotGrow_IsRefused_AndSealsThePasskey_WhichStaysLive(uint incoming)
    {
        using var fixture = new PasskeySignInFixture(storedSignCount: 5);

        var result = await fixture.SignInAsync(new CeremonyTweaks { SignCount = incoming });

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.Passkeys.Verify(p => p.SealCounterRegressionAsync(fixture.Passkey.Id, It.IsAny<DateTime>(), CancellationToken.None), Times.Once);
        fixture.Passkeys.Verify(p => p.TryRecordAssertionAsync(It.IsAny<UserPasskey>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Passkeys.Verify(p => p.TryRevokeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never,
            "the passkey is never removed automatically");
        fixture.Passkey.IsLive.Should().BeTrue();
        fixture.Passkey.LastCounterRegressionAt.Should().NotBeNull();
        fixture.Passkey.SignCount.Should().Be(5, "a regression takes nothing from the answer");
        fixture.CeremonyLog.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning
            && entry.Values.Contains(PasskeySignalCodes.CounterRegressed));
        fixture.VerifyNoSession();
    }

    /// <summary>
    /// T-B3: PK9 runs in this code on the verified counter whatever the library does: a verifier
    /// that accepted a counter that did not grow is still refused, and the passkey sealed.
    /// </summary>
    [Fact]
    public async Task ACounterTheVerifierAccepted_ButThatDidNotGrow_IsStillRefused_AndSealed()
    {
        using var fixture = new PasskeySignInFixture(storedSignCount: 5);
        var accepting = new Mock<IWebAuthnVerifier>();
        accepting.Setup(v => v.CreateAssertionOptions(It.IsAny<PasskeyRelyingParty>())).Returns("{\"challenge\":\"YQ\",\"rpId\":\"example.com\"}");
        accepting.Setup(v => v.VerifyAssertionAsync(It.IsAny<PasskeyRelyingParty>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StoredPasskeyCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerifiedPasskeyAssertion(SignCount: 0, IsBackupEligible: false, IsBackedUp: false, CounterRegressed: false));
        fixture.Verifier = accepting.Object;

        var response = fixture.Authenticator.Assert(await fixture.OptionsAsync(), PasskeySignInFixture.Origin, PasskeySignInFixture.OwnerHandle);
        var result = await fixture.Verify().Handle(fixture.VerifyCommand(response), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, "5 stored, 0 answered: one of them counts and it did not grow");
        fixture.Passkeys.Verify(p => p.SealCounterRegressionAsync(fixture.Passkey.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.VerifyNoSession();
    }

    [Fact]
    public async Task ARecordThatMatchedNoRow_SignsNoOneIn()
    {
        // A removal, a reset or another sign-in got there first (T-B4).
        using var fixture = new PasskeySignInFixture { RecordSucceeds = false };

        var result = await fixture.SignInAsync();

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.VerifyNoSession();
    }

    // ── 8. FA6: the codes of their own ──

    [Fact]
    public async Task WhilePasskeysDoNotRun_NothingIsSpent_AndNoOneSignsIn()
    {
        using var fixture = new PasskeySignInFixture();
        var response = await fixture.AnswerAsync();
        fixture.Feature = PasskeyTestDoubles.Feature(enabled: false);

        var result = await fixture.Verify().Handle(fixture.VerifyCommand(response), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.ConsumeCalls.Should().Be(0);
        fixture.VerifyNoSession();
    }

    [Fact]
    public void TheCommand_NeverPrintsItsHandleOrItsResponse()
    {
        new VerifyPasskeyAssertionCommand("secret-handle", false, "{\"secret\":1}", "203.0.113.7", "Agent", "d", true)
            .ToString().Should().NotContain("secret-handle").And.NotContain("secret");
    }

    private static string WithoutUserHandle(string response)
    {
        var node = JsonNode.Parse(response)!;
        node["response"]!.AsObject().Remove("userHandle");
        return node.ToJsonString();
    }

    private static string WithRawId(string response, string rawId)
    {
        var node = JsonNode.Parse(response)!;
        node["rawId"] = rawId;
        return node.ToJsonString();
    }

    private static string RemovedAnd(PasskeySignInFixture fixture, string response)
    {
        fixture.PasskeyLive = false;
        return response;
    }
}
