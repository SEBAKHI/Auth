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
/// the proof that neither algorithm loads the native crypto library.
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
        SoftwareAuthenticator authenticator, byte[] publicKey, CeremonyTweaks tweaks)
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
                StoredSignatureCounter = 0,
                IsUserHandleOwnerOfCredentialIdCallback = static (_, _) => Task.FromResult(true),
            },
            CancellationToken.None);
    }

    private static async Task<Exception?> CatchAssertionAsync(
        SoftwareAuthenticator authenticator, byte[] publicKey, CeremonyTweaks tweaks)
    {
        try
        {
            await AssertAsync(authenticator, publicKey, tweaks);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
