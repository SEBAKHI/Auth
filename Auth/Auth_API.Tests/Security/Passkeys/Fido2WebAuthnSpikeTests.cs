using System.Diagnostics;
using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Infrastructure.Authentication.Passkeys;
using Auth_API.Tests.Helpers;
using Fido2NetLib;
using Fido2NetLib.Exceptions;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Security.Passkeys;

/// <summary>
/// The S20 spike (PK1 (1-2), D-S20-3 (a)), kept in the suite: a registration and an
/// assertion through the real WebAuthn library with keys this test makes, for ES256 (-7)
/// and RS256 (-257); then each ceremony fed bad input one field at a time, pinning the
/// exception the library raises for each (the list <see cref="Fido2Failures"/> maps); then
/// the proof that neither algorithm loads the native crypto library. PR B adds the sign-in
/// through the adapter: the counter the library refuses, the origins, and the stored key (N4).
/// </summary>
/// <remarks>
/// <para>
/// Recorded 2026-11-05 with Fido2 4.3.2: every bad field raised a
/// <see cref="Fido2VerificationException"/> (with the code pinned below) or, for a field
/// that is not even the expected JSON, a <see cref="JsonException"/>. No other type.
/// </para>
/// <para>
/// The library's managed <c>NSec.Cryptography</c> assembly loads with its own types; the
/// native <c>libsodium</c> it wraps (for EdDSA) is never loaded by ES256 or RS256. The IIS
/// leg (an app pool without a user profile) is the owner's check at the first enabling on a
/// test deployment ([S20-O6]).
/// </para>
/// </remarks>
public class Fido2WebAuthnSpikeTests
{
    private const string RpId = "localhost";
    private const string Origin = "https://localhost:5173";

    private static readonly PasskeyRelyingParty RelyingParty =
        new(RpId, RpId, new HashSet<string>(StringComparer.Ordinal) { Origin, "https://localhost:5174" });

    private static readonly byte[] UserHandle = Enumerable.Range(1, 64).Select(i => (byte)i).ToArray();

    private readonly Fido2WebAuthnVerifier _verifier = new(NullLogger<Fido2WebAuthnVerifier>.Instance);

    public static TheoryData<int> Algorithms => new() { SoftwareAuthenticator.Es256, SoftwareAuthenticator.Rs256 };

    /// <summary>Each bad registration field, and what the library raised for it (the spike's record).</summary>
    public static TheoryData<int, string, string> RegistrationRecord()
    {
        var data = new TheoryData<int, string, string>();
        foreach (var algorithm in new[] { SoftwareAuthenticator.Es256, SoftwareAuthenticator.Rs256 })
        {
            foreach (var (field, _, expected) in RegistrationCases(algorithm))
            {
                data.Add(algorithm, field, expected);
            }
        }

        return data;
    }

    /// <summary>Each bad assertion field, and what the library raised for it.</summary>
    public static TheoryData<int, string, string> AssertionRecord()
    {
        var data = new TheoryData<int, string, string>();
        foreach (var algorithm in new[] { SoftwareAuthenticator.Es256, SoftwareAuthenticator.Rs256 })
        {
            foreach (var (field, _, expected) in AssertionCases())
            {
                data.Add(algorithm, field, expected);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public async Task ARegistrationAndAnAssertion_VerifyThroughTheLibrary(int algorithm)
    {
        using var authenticator = SoftwareAuthenticator.Create(algorithm);

        var registered = await RegisterAsync(authenticator);

        registered.IsError.Should().BeFalse(registered.IsError ? registered.FirstError.Description : "");
        registered.Value.CredentialId.Should().Equal(authenticator.CredentialId);
        registered.Value.PublicKey.Should().Equal(authenticator.CosePublicKey());
        registered.Value.SignCount.Should().Be(0);

        var asserted = await AssertAsync(authenticator, registered.Value.PublicKey, new CeremonyTweaks());
        asserted.CredentialId.Should().Equal(authenticator.CredentialId);
        asserted.SignCount.Should().Be(1);
    }

    [Fact]
    public void TheOptions_OfferES256AndRS256Only_AndAskForADiscoverableUserVerifiedCredential()
    {
        using var options = JsonDocument.Parse(_verifier.CreateRegistrationOptions(
            RelyingParty, new PasskeyRegistrationSubject(UserHandle, "someone@example.com", "Someone"), [new byte[] { 1, 2, 3 }]));
        var root = options.RootElement;

        root.GetProperty("pubKeyCredParams").EnumerateArray().Select(p => p.GetProperty("alg").GetInt32())
            .Should().Equal([-7, -257], "PK1: ES256 and RS256, and nothing else");
        root.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString().Should().Be("required");
        root.GetProperty("authenticatorSelection").GetProperty("requireResidentKey").GetBoolean().Should().BeTrue("what a WebAuthn Level 1 browser reads");
        root.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString().Should().Be("required");
        root.GetProperty("attestation").GetString().Should().Be("none");
        root.GetProperty("timeout").GetInt64().Should().Be(300_000);
        root.GetProperty("rp").GetProperty("id").GetString().Should().Be(RpId);
        root.GetProperty("excludeCredentials").GetArrayLength().Should().Be(1);
        root.GetProperty("user").GetProperty("id").GetString().Should().Be(SoftwareAuthenticator.Base64Url(UserHandle));
        Convert.FromBase64String(Pad(root.GetProperty("challenge").GetString()!)).Should().HaveCount(32);
    }

    [Fact]
    public async Task ACredentialOfAnAlgorithmNotOffered_IsRefused()
    {
        // An RSA key declared as PS256 (-37): a real algorithm, never offered.
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Rs256);

        var registered = await RegisterAsync(authenticator, new CeremonyTweaks { KeyAlgorithm = -37 });

        registered.IsError.Should().BeTrue();
        registered.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
    }

    [Theory]
    [MemberData(nameof(RegistrationRecord))]
    public async Task EachBadRegistrationField_RaisesWhatTheSpikeRecorded_AndMapsToVerificationFailed(
        int algorithm, string field, string expected)
    {
        var tweaks = RegistrationCases(algorithm).Single(c => c.Field == field).Tweaks;

        using var raw = SoftwareAuthenticator.Create(algorithm);
        Describe(await CatchRegistrationAsync(raw, tweaks)).Should().Be(expected, field);

        using var mapped = SoftwareAuthenticator.Create(algorithm);
        var result = await RegisterAsync(mapped, tweaks);
        result.IsError.Should().BeTrue(field);
        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, field);
    }

    [Theory]
    [MemberData(nameof(AssertionRecord))]
    public async Task EachBadAssertionField_RaisesWhatTheSpikeRecorded_OfAClientFaultType(
        int algorithm, string field, string expected)
    {
        var tweaks = AssertionCases().Single(c => c.Field == field).Tweaks;
        using var authenticator = SoftwareAuthenticator.Create(algorithm);
        var registered = await RegisterAsync(authenticator);

        var exception = await CatchAssertionAsync(authenticator, registered.Value.PublicKey, tweaks);

        Describe(exception).Should().Be(expected, field);
        Fido2Failures.IsClientFault(exception!, CancellationToken.None).Should().BeTrue(field);
    }

    [Fact]
    public async Task NoNativeCryptoLibrary_IsLoaded_ByEitherAlgorithm()
    {
        foreach (var algorithm in new[] { SoftwareAuthenticator.Es256, SoftwareAuthenticator.Rs256 })
        {
            using var authenticator = SoftwareAuthenticator.Create(algorithm);
            var registered = await RegisterAsync(authenticator);
            registered.IsError.Should().BeFalse();
            await AssertAsync(authenticator, registered.Value.PublicKey, new CeremonyTweaks());
        }

        var modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Select(module => module.ModuleName)
            .ToList();

        modules.Should().Contain(name => name.StartsWith("Fido2", StringComparison.OrdinalIgnoreCase),
            "the scan must see the library's own module at all");
        modules.Should().NotContain(name => name.Contains("sodium", StringComparison.OrdinalIgnoreCase),
            "ES256 and RS256 run on the platform's own cryptography; the native libsodium (EdDSA) must never load");
    }

    /// <summary>
    /// REVIEW 150 F9: a genuine COSE OKP key (kty 1, crv Ed25519, alg -8), the key type a client
    /// picks for EdDSA, and a malformed one. The relabelled EC2 and RSA keys above never reach
    /// the library's OKP code, which is the code that loads the native libsodium.
    /// </summary>
    public static TheoryData<string, string> OkpKeys => new()
    {
        // RFC 8032 §7.1, test 1: the public key.
        { "an Ed25519 key (RFC 8032 test 1)", "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a" },
        { "an Ed25519 point of 31 bytes", "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f70751" },
    };

    /// <remarks>
    /// <para>
    /// Recorded 2026-11-05 with Fido2 4.3.2, the library alone: the RFC 8032 key raised
    /// <c>CredentialAlgorithmRequirementNotMet</c> and the 31-byte point
    /// <c>MalformedAttestationObject</c> — both client faults — <b>but the first loaded
    /// <c>libsodium.DLL</c></b>: the library reads the key through NSec before it refuses the
    /// algorithm. So "no native library loads" (D-S20-3 (a)) held only for the keys this
    /// server offers, and the adapter now refuses any key type but EC2 and RSA before the
    /// library runs (<c>AttestedKeyType</c>). The library alone is not run on an OKP key here:
    /// it would load libsodium into the test process for every later test.
    /// </para>
    /// <para>Deliberate break, verified red: the key-type check removed from the adapter.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OkpKeys))]
    public async Task AGenuineOkpKey_IsRefused_AsTheClientsFault_WithoutLoadingTheNativeLibrary(string field, string publicKeyHex)
    {
        var tweaks = new CeremonyTweaks { CosePublicKey = OkpKey(Convert.FromHexString(publicKeyHex)) };
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);

        var result = await RegisterAsync(authenticator, tweaks);

        result.IsError.Should().BeTrue(field);
        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, $"{field}: a client's key is never a 500");
        LoadedModules().Should().NotContain(name => name.Contains("sodium", StringComparison.OrdinalIgnoreCase),
            $"{field}: an OKP key never reaches the library, which would read it through libsodium");
    }

    /// <summary>
    /// REVIEW 150 F10 (b): options stored under another RP ID — a restart with a new
    /// <c>Passkeys:RpId</c> inside a ceremony's five minutes — are refused before the library
    /// runs, whichever RP ID the device answered for, so a credential is never bound to one RP
    /// ID and stored under another.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the comparison dropped from the adapter.</remarks>
    [Theory]
    [InlineData("old.example.com")]
    [InlineData("example.com")]
    public async Task StoredOptionsOfAnotherRpId_AreRefused_BeforeTheLibraryRuns(string answeredFor)
    {
        var origin = "https://console.example.com";
        var stored = new PasskeyRelyingParty("old.example.com", "old.example.com", new HashSet<string>(StringComparer.Ordinal) { "https://console.old.example.com" });
        var inForce = new PasskeyRelyingParty("example.com", "example.com", new HashSet<string>(StringComparer.Ordinal) { origin, "https://console.old.example.com" });
        var options = _verifier.CreateRegistrationOptions(stored, new PasskeyRegistrationSubject(UserHandle, "someone@example.com", "Someone"), []);
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var response = authenticator.Register(
            options, answeredFor == "example.com" ? origin : "https://console.old.example.com", new CeremonyTweaks { RpId = answeredFor });

        var result = await _verifier.VerifyRegistrationAsync(inForce, options, response, CancellationToken.None);

        result.IsError.Should().BeTrue($"the device answered for {answeredFor}");
        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
    }

    [Fact]
    public async Task OnlyAnEc2OrRsaKeyWhoseTypeReadsOnce_ReachesTheLibrary()
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var genuine = authenticator.CosePublicKey();

        // kty given twice (2, then 1), and kty given as text: each could read one way here and
        // another in the library, so each is refused before it.
        var twice = new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Lax);
        twice.WriteStartMap(2);
        twice.WriteInt32(1);
        twice.WriteInt32(2);
        twice.WriteInt32(1);
        twice.WriteInt32(1);
        twice.WriteEndMap();
        var asText = new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Lax);
        asText.WriteStartMap(1);
        asText.WriteInt32(1);
        asText.WriteTextString("EC2");
        asText.WriteEndMap();

        (await RegisterAsync(authenticator, new CeremonyTweaks { CosePublicKey = genuine })).IsError.Should().BeFalse();
        foreach (var key in new[] { twice.Encode(), asText.Encode() })
        {
            (await RegisterAsync(authenticator, new CeremonyTweaks { CosePublicKey = key })).FirstError.Code
                .Should().Be(PasskeyErrors.VerificationFailed.Code);
        }

        (await RegisterAsync(authenticator, new CeremonyTweaks { OmitAttestedCredentialFlag = true })).FirstError.Code
            .Should().Be(PasskeyErrors.VerificationFailed.Code, "no attested credential, no key to read");
    }

    /// <summary>
    /// REVIEW-FIXES 150 F15: only attestation none, or packed self-attestation with ES256 or
    /// RS256, reaches the library. Its packed verifier reads an x5c certificate's key with whatever
    /// algorithm the statement names, before any other check, so an Ed25519 certificate would load
    /// libsodium. Called directly: no library runs.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the x5c refusal dropped (a certificate with ES256 passes).</remarks>
    [Theory]
    [InlineData("none", null, false, false, false, true)]
    [InlineData("packed", -7, true, false, false, true)]
    [InlineData("packed", -257, true, false, false, true)]
    [InlineData("packed", -8, true, false, false, false)]
    [InlineData("packed", null, true, false, false, false)]
    [InlineData("packed", -7, true, true, false, false)]
    [InlineData("packed", -8, true, true, false, false)]
    [InlineData("packed", -7, true, false, true, false)]
    [InlineData("tpm", -257, true, false, false, false)]
    [InlineData("fido-u2f", null, true, true, false, false)]
    public void TheAttestationStatement_OnlyNoneOrPackedSelfAttestation_ReachesTheLibrary(
        string format, int? algorithm, bool signature, bool certificate, bool ecdaaKey, bool reaches)
    {
        var response = AttestationResponse(writer =>
        {
            writer.WriteStartMap(3);
            writer.WriteTextString("fmt");
            writer.WriteTextString(format);
            writer.WriteTextString("attStmt");
            WriteStatement(writer, algorithm, signature, certificate, ecdaaKey);
            writer.WriteTextString("authData");
            writer.WriteByteString(new byte[37]);
            writer.WriteEndMap();
        });

        AttestedKeyType.IsNoneOrPackedSelfAttestation(response).Should().Be(reaches,
            $"{format} with alg {algorithm?.ToString() ?? "absent"}, x5c {certificate}, ecdaaKeyId {ecdaaKey}");
    }

    /// <summary>
    /// REVIEW-FIXES 150 F15: an entry given twice — the format, or the statement's alg — could read
    /// one way here and another in the library, so it is refused before it.
    /// </summary>
    [Fact]
    public void AnAttestationEntryGivenTwice_IsRefused()
    {
        var formatTwice = AttestationResponse(writer =>
        {
            writer.WriteStartMap(4);
            writer.WriteTextString("fmt");
            writer.WriteTextString("none");
            writer.WriteTextString("fmt");
            writer.WriteTextString("tpm");
            writer.WriteTextString("attStmt");
            WriteStatement(writer, algorithm: null, signature: false, certificate: false, ecdaaKey: false);
            writer.WriteTextString("authData");
            writer.WriteByteString(new byte[37]);
            writer.WriteEndMap();
        });
        var algorithmTwice = AttestationResponse(writer =>
        {
            writer.WriteStartMap(3);
            writer.WriteTextString("fmt");
            writer.WriteTextString("packed");
            writer.WriteTextString("attStmt");
            writer.WriteStartMap(2);
            writer.WriteTextString("alg");
            writer.WriteInt32(-7);
            writer.WriteTextString("alg");
            writer.WriteInt32(-8);
            writer.WriteEndMap();
            writer.WriteTextString("authData");
            writer.WriteByteString(new byte[37]);
            writer.WriteEndMap();
        });

        AttestedKeyType.IsNoneOrPackedSelfAttestation(formatTwice).Should().BeFalse();
        AttestedKeyType.IsNoneOrPackedSelfAttestation(algorithmTwice).Should().BeFalse();
    }

    /// <summary>
    /// REVIEW-FIXES 150 F15: the verifier asks before the library. A packed statement carrying a
    /// certificate chain is refused with the statement as the reason the log names, so the
    /// library's packed verifier never reads the certificate.
    /// </summary>
    [Fact]
    public async Task APackedStatementWithACertificate_IsRefusedByTheVerifier_BeforeTheLibraryRuns()
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var log = new CapturingLogger<Fido2WebAuthnVerifier>();
        var verifier = new Fido2WebAuthnVerifier(log);
        var options = verifier.CreateRegistrationOptions(
            RelyingParty, new PasskeyRegistrationSubject(UserHandle, "someone@example.com", "Someone"), []);
        var response = authenticator.Register(options, Origin, new CeremonyTweaks
        {
            AttestationFormat = "packed",
            AttestationStatement = writer => WriteStatement(writer, algorithm: -7, signature: true, certificate: true, ecdaaKey: false),
        });

        var result = await verifier.VerifyRegistrationAsync(RelyingParty, options, response, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        log.Entries.Should().ContainSingle()
            .Which.Message.Should().Contain("the attestation is not none or packed self-attestation", "refused by the check, not by the library");
    }

    /// <summary>
    /// REVIEW-FIXES 150 F18 (f): a COSE kty given twice is refused however each is encoded — OKP
    /// (1) then EC2 (2) written non-minimally (0x18 0x02), and the reverse — called directly.
    /// </summary>
    /// <remarks>Deliberate break, verified red: <c>keyType is not null</c> dropped (the second kty read as the key's).</remarks>
    [Theory]
    [InlineData(new byte[] { 0xA2, 0x01, 0x01, 0x01, 0x18, 0x02 })]
    [InlineData(new byte[] { 0xA2, 0x01, 0x18, 0x02, 0x01, 0x01 })]
    public void AKeyTypeGivenTwice_IsRefused_HoweverEachIsEncoded(byte[] coseKey)
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var response = authenticator.Register(RegistrationOptions(), Origin, new CeremonyTweaks { CosePublicKey = coseKey });

        AttestedKeyType.IsEc2OrRsa(response).Should().BeFalse();
    }

    // ── S20 PR B: the sign-in, through the adapter ──

    /// <summary>
    /// An assertion of each algorithm through the adapter (IN 1): the counter, the BE and BS
    /// flags read back, nothing refused.
    /// </summary>
    [Theory]
    [MemberData(nameof(Algorithms))]
    public async Task AnAssertion_VerifiesThroughTheAdapter_WithTheCounterAndBothFlags(int algorithm)
    {
        using var authenticator = SoftwareAuthenticator.Create(algorithm);
        var registered = await RegisterAsync(authenticator);
        var options = _verifier.CreateAssertionOptions(RelyingParty);
        var response = authenticator.Assert(options, Origin, UserHandle, new CeremonyTweaks { BackupEligible = true, BackedUp = true });

        var verified = await _verifier.VerifyAssertionAsync(
            RelyingParty, options, response, Stored(registered.Value.PublicKey, signCount: 0), CancellationToken.None);

        verified.IsError.Should().BeFalse(verified.IsError ? verified.FirstError.Description : "");
        verified.Value.SignCount.Should().Be(1);
        verified.Value.IsBackupEligible.Should().BeTrue("the BE flag is read from the verified authenticator data");
        verified.Value.IsBackedUp.Should().BeTrue();
        verified.Value.CounterRegressed.Should().BeFalse();
    }

    /// <remarks>
    /// <para>
    /// Recorded 2026-11-06 with Fido2 4.3.2 (IN 1): a counter that did not grow — stored 5 and
    /// answered 3, 5 or 0 — raises <see cref="Fido2VerificationException"/> with
    /// <see cref="Fido2ErrorCode.InvalidSignCount"/>; two zeros pass. And it is raised only once
    /// the signature verified: the same counter under a corrupt signature raises
    /// <see cref="Fido2ErrorCode.InvalidSignature"/>. So the adapter's distinct «counter regressed»
    /// outcome, which seals the passkey, is never reached by a forged answer.
    /// </para>
    /// <para>Deliberate break, verified red: the adapter's InvalidSignCount catch removed (the plain refusal returned).</para>
    /// </remarks>
    [Theory]
    [InlineData(5u, 3u, "InvalidSignCount")]
    [InlineData(5u, 5u, "InvalidSignCount")]
    [InlineData(5u, 0u, "InvalidSignCount")]
    [InlineData(0u, 0u, "accepted")]
    [InlineData(0u, 7u, "accepted")]
    public async Task ACounterThatDidNotGrow_IsInvalidSignCount_AndTheAdapterReportsIt_NotAsAPlainRefusal(
        uint stored, uint incoming, string expected)
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var registered = await RegisterAsync(authenticator);
        var tweaks = new CeremonyTweaks { SignCount = incoming };

        var raw = await CatchAssertionAsync(authenticator, registered.Value.PublicKey, tweaks, stored);
        (raw is null ? "accepted" : ((Fido2VerificationException)raw).Code.ToString()).Should().Be(expected);

        var options = _verifier.CreateAssertionOptions(RelyingParty);
        var verified = await _verifier.VerifyAssertionAsync(
            RelyingParty, options, authenticator.Assert(options, Origin, UserHandle, tweaks),
            Stored(registered.Value.PublicKey, stored), CancellationToken.None);

        verified.IsError.Should().BeFalse("a counter that did not grow is its own outcome, never Passkey.VerificationFailed");
        verified.Value.CounterRegressed.Should().Be(expected != "accepted");
    }

    [Fact]
    public async Task ACounterThatDidNotGrow_UnderACorruptSignature_IsTheSignaturesRefusal()
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var registered = await RegisterAsync(authenticator);
        var tweaks = new CeremonyTweaks { SignCount = 3, CorruptSignature = true };

        var raw = await CatchAssertionAsync(authenticator, registered.Value.PublicKey, tweaks, storedCounter: 5);
        ((Fido2VerificationException)raw!).Code.Should().Be(Fido2ErrorCode.InvalidSignature,
            "the library checks the signature before the counter");

        var options = _verifier.CreateAssertionOptions(RelyingParty);
        var verified = await _verifier.VerifyAssertionAsync(
            RelyingParty, options, authenticator.Assert(options, Origin, UserHandle, tweaks),
            Stored(registered.Value.PublicKey, signCount: 5), CancellationToken.None);
        verified.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, "a forged answer never seals a passkey");
    }

    [Theory]
    [MemberData(nameof(AssertionRecord))]
    public async Task EachBadAssertionField_MapsToVerificationFailed_ThroughTheAdapter(int algorithm, string field, string _)
    {
        var tweaks = AssertionCases().Single(c => c.Field == field).Tweaks;
        using var authenticator = SoftwareAuthenticator.Create(algorithm);
        var registered = await RegisterAsync(authenticator);
        var options = _verifier.CreateAssertionOptions(RelyingParty);

        var verified = await _verifier.VerifyAssertionAsync(
            RelyingParty, options, authenticator.Assert(options, Origin, UserHandle, tweaks),
            Stored(registered.Value.PublicKey, signCount: 0), CancellationToken.None);

        verified.IsError.Should().BeTrue(field);
        verified.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, field);
    }

    /// <summary>
    /// PK4: only the exact first-party origins sign in. The apex, a sibling host, another port
    /// and plain http are refused by the library, and so is a ceremony inside another page's
    /// frame (topOrigin).
    /// </summary>
    /// <remarks>Deliberate break, verified red: the origins handed to the library made the CORS list's superset.</remarks>
    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://example.com")]
    [InlineData("https://evil.localhost:5173")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:5175")]
    public async Task AnAssertionFromAnyOtherOrigin_IsRefused(string origin)
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var registered = await RegisterAsync(authenticator);
        var options = _verifier.CreateAssertionOptions(RelyingParty);

        var verified = await _verifier.VerifyAssertionAsync(
            RelyingParty, options, authenticator.Assert(options, origin, UserHandle),
            Stored(registered.Value.PublicKey, signCount: 0), CancellationToken.None);

        verified.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code, origin);
    }

    [Fact]
    public async Task AnAssertionInsideAnotherPagesFrame_IsRefused()
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var registered = await RegisterAsync(authenticator);
        var options = _verifier.CreateAssertionOptions(RelyingParty);

        var verified = await _verifier.VerifyAssertionAsync(
            RelyingParty, options, authenticator.Assert(options, Origin, UserHandle, new CeremonyTweaks { TopOrigin = "https://example.com" }),
            Stored(registered.Value.PublicKey, signCount: 0), CancellationToken.None);

        verified.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
    }

    /// <summary>
    /// N4: the stored key is the server's own. One that cannot be read, or whose key type is not
    /// EC2 or RSA (registration stores no other), is a fault of the server: it throws, named by
    /// the passkey's row id, and never becomes the client's <c>Passkey.VerificationFailed</c> —
    /// even though, inside the library, its parse would raise a type the client-fault list holds.
    /// An OKP key never reaches the library, which would read it through libsodium.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the stored key's parse moved back inside the mapped call (EC2 without y became VerificationFailed).</remarks>
    [Fact]
    public async Task AStoredKeyThatCannotBeRead_IsAServerFault_NeverVerificationFailed()
    {
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var options = _verifier.CreateAssertionOptions(RelyingParty);
        var response = authenticator.Assert(options, Origin, UserHandle);
        var passkeyId = Guid.NewGuid();

        var ec2WithoutY = new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Lax);
        ec2WithoutY.WriteStartMap(4);
        ec2WithoutY.WriteInt32(1); ec2WithoutY.WriteInt32(2);
        ec2WithoutY.WriteInt32(3); ec2WithoutY.WriteInt32(-7);
        ec2WithoutY.WriteInt32(-1); ec2WithoutY.WriteInt32(1);
        ec2WithoutY.WriteInt32(-2); ec2WithoutY.WriteByteString(new byte[32]);
        ec2WithoutY.WriteEndMap();

        foreach (var (name, key) in new[]
                 {
                     ("an EC2 key without its y", ec2WithoutY.Encode()),
                     ("an OKP key (EdDSA)", OkpKey(Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"))),
                     ("bytes that are not CBOR", new byte[] { 0xFF, 0x00 }),
                     ("nothing", Array.Empty<byte>()),
                 })
        {
            var act = () => _verifier.VerifyAssertionAsync(
                RelyingParty, options, response, new StoredPasskeyCredential(passkeyId, key, 0), CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<Exception>(name);
            Fido2Failures.IsClientFault(thrown.Which, CancellationToken.None).Should().BeFalse(name);
        }

        LoadedModules().Should().NotContain(module => module.Contains("sodium", StringComparison.OrdinalIgnoreCase),
            "an OKP key is refused before the library reads it");
    }

    [Fact]
    public async Task StoredAssertionOptionsOfAnotherRpId_AreRefused_BeforeTheLibraryRuns()
    {
        var stored = new PasskeyRelyingParty("old.example.com", "old.example.com", new HashSet<string>(StringComparer.Ordinal) { "https://accounts.old.example.com" });
        var inForce = new PasskeyRelyingParty("example.com", "example.com", new HashSet<string>(StringComparer.Ordinal) { "https://accounts.example.com" });
        using var authenticator = SoftwareAuthenticator.Create(SoftwareAuthenticator.Es256);
        var options = _verifier.CreateAssertionOptions(stored);
        var response = authenticator.Assert(options, "https://accounts.example.com", UserHandle, new CeremonyTweaks { RpId = "example.com" });

        var verified = await _verifier.VerifyAssertionAsync(
            inForce, options, response, Stored(authenticator.CosePublicKey(), signCount: 0), CancellationToken.None);

        verified.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
    }

    private static StoredPasskeyCredential Stored(byte[] publicKey, long signCount) => new(Guid.NewGuid(), publicKey, signCount);

    /// <summary>A registration response whose attestation object the given writer writes.</summary>
    private static string AttestationResponse(Action<System.Formats.Cbor.CborWriter> attestationObject)
    {
        var writer = new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Lax);
        attestationObject(writer);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["response"] = new System.Text.Json.Nodes.JsonObject
            {
                ["attestationObject"] = SoftwareAuthenticator.Base64Url(writer.Encode()),
            },
        }.ToJsonString();
    }

    /// <summary>An attestation statement map: alg, sig, an x5c chain and an ECDAA key id, each when asked.</summary>
    private static void WriteStatement(
        System.Formats.Cbor.CborWriter writer, int? algorithm, bool signature, bool certificate, bool ecdaaKey)
    {
        writer.WriteStartMap((algorithm is null ? 0 : 1) + (signature ? 1 : 0) + (certificate ? 1 : 0) + (ecdaaKey ? 1 : 0));
        if (algorithm is { } alg)
        {
            writer.WriteTextString("alg");
            writer.WriteInt32(alg);
        }

        if (signature)
        {
            writer.WriteTextString("sig");
            writer.WriteByteString(new byte[64]);
        }

        if (certificate)
        {
            writer.WriteTextString("x5c");
            writer.WriteStartArray(1);
            writer.WriteByteString(new byte[] { 0x30, 0x03, 0x02, 0x01, 0x01 });
            writer.WriteEndArray();
        }

        if (ecdaaKey)
        {
            writer.WriteTextString("ecdaaKeyId");
            writer.WriteByteString(new byte[32]);
        }

        writer.WriteEndMap();
    }

    /// <summary>A COSE OKP public key: kty 1 (OKP), alg -8 (EdDSA), crv 6 (Ed25519), x.</summary>
    private static byte[] OkpKey(byte[] x)
    {
        var writer = new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Lax);
        writer.WriteStartMap(4);
        writer.WriteInt32(1);
        writer.WriteInt32(1);
        writer.WriteInt32(3);
        writer.WriteInt32(-8);
        writer.WriteInt32(-1);
        writer.WriteInt32(6);
        writer.WriteInt32(-2);
        writer.WriteByteString(x);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static List<string> LoadedModules() =>
        Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToList();

    /// <summary>The bad registration fields, each with what the library raised (Fido2 4.3.2).</summary>
    internal static IEnumerable<(string Field, CeremonyTweaks Tweaks, string Expected)> RegistrationCases(int algorithm) =>
    [
        ("challenge (another)", new CeremonyTweaks { Challenge = SoftwareAuthenticator.Base64Url(new byte[32]) }, Code(Fido2ErrorCode.InvalidAuthenticatorResponseChallenge)),
        ("clientData type (webauthn.get)", new CeremonyTweaks { ClientDataType = "webauthn.get" }, Code(Fido2ErrorCode.InvalidAttestationResponse)),
        ("RP ID hash (another RP)", new CeremonyTweaks { RpId = "example.com" }, Code(Fido2ErrorCode.InvalidRpidHash)),
        ("user verification (absent)", new CeremonyTweaks { UserVerified = false }, Code(Fido2ErrorCode.UserVerificationRequirementNotMet)),
        ("user presence (absent)", new CeremonyTweaks { UserPresent = false }, Code(Fido2ErrorCode.UserPresentFlagNotSet)),
        ("crossOrigin (true)", new CeremonyTweaks { CrossOrigin = true }, Code(Fido2ErrorCode.CrossOriginRequestNotAllowed)),
        ("topOrigin (a parent page)", new CeremonyTweaks { CrossOrigin = true, TopOrigin = "https://example.com" }, Code(Fido2ErrorCode.CrossOriginRequestNotAllowed)),
        ("algorithm (EdDSA declared)", new CeremonyTweaks { KeyAlgorithm = -8 },
            algorithm == SoftwareAuthenticator.Es256 ? Code(Fido2ErrorCode.InvalidCredentialPublicKey) : Code(Fido2ErrorCode.CredentialAlgorithmRequirementNotMet)),
        ("algorithm (ES384 declared)", new CeremonyTweaks { KeyAlgorithm = -35 },
            algorithm == SoftwareAuthenticator.Es256 ? Code(Fido2ErrorCode.InvalidCredentialPublicKey) : Code(Fido2ErrorCode.CredentialAlgorithmRequirementNotMet)),
        ("attested-credential flag (absent)", new CeremonyTweaks { OmitAttestedCredentialFlag = true }, Code(Fido2ErrorCode.InvalidAuthenticatorData)),
        ("authenticator data (truncated)", new CeremonyTweaks { TruncateAuthData = true }, Code(Fido2ErrorCode.InvalidAuthenticatorData)),
        ("attestation format (unknown)", new CeremonyTweaks { AttestationFormat = "made-up" }, Code(Fido2ErrorCode.UnknownAttestationType)),
        ("attestation format (packed, no statement)", new CeremonyTweaks { AttestationFormat = "packed" }, Code(Fido2ErrorCode.InvalidAttestation)),
        ("attestation object (not CBOR)", new CeremonyTweaks { RawAttestationObject = SoftwareAuthenticator.Base64Url([0xFF, 0x00, 0x13]) }, Code(Fido2ErrorCode.InvalidAttestationObject)),
        ("attestation object (not base64url)", new CeremonyTweaks { RawAttestationObject = "%%%" }, nameof(JsonException)),
        ("clientDataJSON (not JSON)", new CeremonyTweaks { RawClientDataJson = SoftwareAuthenticator.Base64Url("not json"u8.ToArray()) }, Code(Fido2ErrorCode.MalformedAuthenticatorResponse)),
        ("credential type (another)", new CeremonyTweaks { CredentialType = "password" }, nameof(JsonException)),
        ("public key (not COSE)", new CeremonyTweaks { CosePublicKey = [0xA0] }, Code(Fido2ErrorCode.MalformedAttestationObject)),
        ("response id (not the attested id)", new CeremonyTweaks { ResponseCredentialId = new byte[32] }, Code(Fido2ErrorCode.InvalidAttestationResponse)),
    ];

    /// <summary>The bad assertion fields, each with what the library raised (Fido2 4.3.2).</summary>
    internal static IEnumerable<(string Field, CeremonyTweaks Tweaks, string Expected)> AssertionCases() =>
    [
        ("challenge (another)", new CeremonyTweaks { Challenge = SoftwareAuthenticator.Base64Url(new byte[32]) }, Code(Fido2ErrorCode.InvalidAuthenticatorResponseChallenge)),
        ("clientData type (webauthn.create)", new CeremonyTweaks { ClientDataType = "webauthn.create" }, Code(Fido2ErrorCode.InvalidAssertionResponse)),
        ("RP ID hash (another RP)", new CeremonyTweaks { RpId = "example.com" }, Code(Fido2ErrorCode.InvalidRpidHash)),
        ("user verification (absent)", new CeremonyTweaks { UserVerified = false }, Code(Fido2ErrorCode.UserVerificationRequirementNotMet)),
        // REVIEW 151 F10 (S5): as the registration list has it.
        ("user presence (absent)", new CeremonyTweaks { UserPresent = false }, Code(Fido2ErrorCode.UserPresentFlagNotSet)),
        ("crossOrigin (true)", new CeremonyTweaks { CrossOrigin = true }, Code(Fido2ErrorCode.CrossOriginRequestNotAllowed)),
        ("signature (corrupt)", new CeremonyTweaks { CorruptSignature = true }, Code(Fido2ErrorCode.InvalidSignature)),
        ("authenticator data (not base64url)", new CeremonyTweaks { RawAuthenticatorData = "%%%" }, nameof(JsonException)),
        ("authenticator data (short)", new CeremonyTweaks { RawAuthenticatorData = SoftwareAuthenticator.Base64Url(new byte[5]) }, Code(Fido2ErrorCode.InvalidAuthenticatorData)),
        ("clientDataJSON (not JSON)", new CeremonyTweaks { RawClientDataJson = SoftwareAuthenticator.Base64Url("not json"u8.ToArray()) }, Code(Fido2ErrorCode.MalformedAuthenticatorResponse)),
        ("credential type (another)", new CeremonyTweaks { CredentialType = "password" }, nameof(JsonException)),
    ];

    private static string Code(Fido2ErrorCode code) => $"{nameof(Fido2VerificationException)} {code}";

    private static string Describe(Exception? exception) =>
        exception is null ? "accepted" : Fido2Failures.Describe(exception);

    private static string Pad(string base64Url)
    {
        var standard = base64Url.Replace('-', '+').Replace('_', '/');
        return standard.PadRight(standard.Length + ((4 - (standard.Length % 4)) % 4), '=');
    }

    private static Fido2 Library() => new(new Fido2Configuration
    {
        RPID = RpId,
        RPName = RpId,
        Origins = RelyingParty.Origins.ToHashSet(StringComparer.Ordinal),
        ChallengeSize = 32,
        AllowCrossOriginRequests = false,
    });

    private string RegistrationOptions() =>
        _verifier.CreateRegistrationOptions(
            RelyingParty, new PasskeyRegistrationSubject(UserHandle, "someone@example.com", "Someone"), []);

    private async Task<ErrorOr.ErrorOr<VerifiedPasskeyCredential>> RegisterAsync(
        SoftwareAuthenticator authenticator, CeremonyTweaks? tweaks = null)
    {
        var options = RegistrationOptions();
        var response = authenticator.Register(options, Origin, tweaks);
        return await _verifier.VerifyRegistrationAsync(RelyingParty, options, response, CancellationToken.None);
    }

    // The library alone, without the adapter's mapping, so the record names what it raised.
    private async Task<Exception?> CatchRegistrationAsync(SoftwareAuthenticator authenticator, CeremonyTweaks tweaks)
    {
        var options = RegistrationOptions();
        var response = authenticator.Register(options, Origin, tweaks);
        try
        {
            await Library().MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(response)!,
                    OriginalOptions = CredentialCreateOptions.FromJson(options),
                    IsCredentialIdUniqueToUserCallback = static (_, _) => Task.FromResult(true),
                },
                CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<VerifyAssertionResult> AssertAsync(
        SoftwareAuthenticator authenticator, byte[] publicKey, CeremonyTweaks tweaks, uint storedCounter = 0)
    {
        var library = Library();
        var options = library.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        var response = authenticator.Assert(options.ToJson(), Origin, UserHandle, tweaks);

        return await library.MakeAssertionAsync(
            new MakeAssertionParams
            {
                AssertionResponse = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(response)!,
                OriginalOptions = options,
                StoredPublicKey = publicKey,
                StoredSignatureCounter = storedCounter,
                IsUserHandleOwnerOfCredentialIdCallback = static (_, _) => Task.FromResult(true),
            },
            CancellationToken.None);
    }

    private static async Task<Exception?> CatchAssertionAsync(
        SoftwareAuthenticator authenticator, byte[] publicKey, CeremonyTweaks tweaks, uint storedCounter = 0)
    {
        try
        {
            await AssertAsync(authenticator, publicKey, tweaks, storedCounter);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
