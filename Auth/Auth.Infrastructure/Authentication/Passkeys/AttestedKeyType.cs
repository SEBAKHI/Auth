using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Text.Json;

namespace Auth.Infrastructure.Authentication.Passkeys;

/// <summary>
/// Reads what a registration response attests, before the WebAuthn library sees the response:
/// the key type of the credential (REVIEW 150 F9), and the attestation statement (REVIEW-FIXES
/// 150 F15). Only an EC2 or an RSA key, attested by <c>none</c> or by <c>packed</c>
/// self-attestation with ES256 or RS256, may reach it.
/// </summary>
/// <remarks>
/// <para>
/// Fido2 4.3.2 reads an OKP (EdDSA) key through NSec, which loads the native libsodium, before
/// it refuses the key's algorithm — the spike shows it with an RFC 8032 key
/// (<c>Fido2WebAuthnSpikeTests</c>). The options offer ES256 and RS256 only, but a client
/// chooses what it sends, and D-S20-3 (a) rests on no native library ever loading. So a
/// credential key whose COSE <c>kty</c> is not 2 (EC2) or 3 (RSA) is refused here, and the
/// library never parses it.
/// </para>
/// <para>
/// The same holds for the attestation statement: the library's packed verifier reads the
/// key of an <c>x5c</c> certificate with whatever <c>alg</c> the statement names, before any
/// other check, so an Ed25519 certificate would load libsodium too. The server asks for
/// attestation <c>none</c>, and a client that honours it sends <c>none</c> or <c>packed</c>
/// self-attestation with no certificate (WebAuthn, "Create a New Credential", the step for
/// attestation <c>none</c>). Anything else — another format, a certificate chain (<c>x5c</c>),
/// an ECDAA key (<c>ecdaaKeyId</c>), a packed <c>alg</c> other than -7 or -257 — is refused
/// here; no conforming browser is.
/// </para>
/// <para>
/// Strict on purpose: anything this cannot read — no attested credential, a truncated
/// authenticator-data blob, a key given twice in the attestation object, in the statement or
/// as the COSE <c>kty</c>, a <c>kty</c> given as text, CBOR that does not parse — is refused
/// too. A browser's response always reads; one that does not is the client's fault, and
/// refusing it costs the same <c>Passkey.VerificationFailed</c> the library would have
/// answered. Only a closed list of parse failures is caught: it reads what the client sent and
/// nothing else, so it never throws.
/// </para>
/// </remarks>
internal static class AttestedKeyType
{
    private const int RpIdHashBytes = 32;
    private const int FlagsOffset = RpIdHashBytes;
    private const int AttestedCredentialDataOffset = RpIdHashBytes + 1 + 4;
    private const int AaguidBytes = 16;
    private const byte AttestedCredentialDataFlag = 0x40;

    private const int CoseKeyTypeLabel = 1;
    private const int CoseKeyTypeEc2 = 2;
    private const int CoseKeyTypeRsa = 3;

    private const int CoseAlgorithmEs256 = -7;
    private const int CoseAlgorithmRs256 = -257;

    private const string FormatNone = "none";
    private const string FormatPacked = "packed";

    /// <summary>
    /// Whether the response attests a credential whose public key is an EC2 or an RSA key.
    /// False for every other key type, and for anything that cannot be read.
    /// </summary>
    public static bool IsEc2OrRsa(string responseJson)
    {
        try
        {
            var attestation = ReadAttestationObject(responseJson);
            return attestation?.AuthData is { } authData
                && KeyTypeOf(authData) is CoseKeyTypeEc2 or CoseKeyTypeRsa;
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the response carries attestation <c>none</c>, or <c>packed</c> self-attestation
    /// with ES256 or RS256: no <c>x5c</c>, no <c>ecdaaKeyId</c>. False for any other format or
    /// statement, and for anything that cannot be read.
    /// </summary>
    public static bool IsNoneOrPackedSelfAttestation(string responseJson)
    {
        try
        {
            if (ReadAttestationObject(responseJson) is not { Format: FormatNone or FormatPacked, Statement: { } encoded } attestation
                || ReadStatement(encoded) is not { HasCertificate: false } statement)
            {
                return false;
            }

            return attestation.Format == FormatNone
                || statement.Algorithm is CoseAlgorithmEs256 or CoseAlgorithmRs256;
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            return false;
        }
    }

    private static bool IsParseFailure(Exception ex) =>
        ex is JsonException or FormatException or CborContentException or InvalidOperationException or OverflowException;

    /// <summary>The three entries of an attestation object.</summary>
    private sealed record AttestationObject(string? Format, ReadOnlyMemory<byte>? Statement, byte[]? AuthData);

    /// <summary>What the statement carries that decides whether it may reach the library.</summary>
    private sealed record AttestationStatement(int? Algorithm, bool HasCertificate);

    /// <summary>
    /// The attestation object of a registration response, read once: its <c>fmt</c>,
    /// <c>attStmt</c> and <c>authData</c>; null when the response holds none, or one of them is
    /// given twice or as the wrong type.
    /// </summary>
    private static AttestationObject? ReadAttestationObject(string responseJson)
    {
        using var response = JsonDocument.Parse(responseJson);
        if (response.RootElement.ValueKind != JsonValueKind.Object
            || !response.RootElement.TryGetProperty("response", out var attestation)
            || attestation.ValueKind != JsonValueKind.Object
            || !attestation.TryGetProperty("attestationObject", out var encoded)
            || encoded.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var reader = new CborReader(Base64Url.DecodeFromChars(encoded.GetString()), CborConformanceMode.Lax);
        var entries = reader.ReadStartMap();
        string? format = null;
        ReadOnlyMemory<byte>? statement = null;
        byte[]? authData = null;
        for (var index = 0; entries is null ? reader.PeekState() != CborReaderState.EndMap : index < entries; index++)
        {
            if (reader.PeekState() != CborReaderState.TextString)
            {
                reader.SkipValue();
                reader.SkipValue();
                continue;
            }

            // An entry given twice could read one way here and another in the library.
            switch (reader.ReadTextString())
            {
                case "fmt":
                    if (format is not null || reader.PeekState() != CborReaderState.TextString)
                    {
                        return null;
                    }

                    format = reader.ReadTextString();
                    break;

                case "attStmt":
                    if (statement is not null || reader.PeekState() != CborReaderState.StartMap)
                    {
                        return null;
                    }

                    statement = reader.ReadEncodedValue();
                    break;

                case "authData":
                    if (authData is not null)
                    {
                        return null;
                    }

                    authData = reader.ReadByteString();
                    break;

                default:
                    reader.SkipValue();
                    break;
            }
        }

        return new AttestationObject(format, statement, authData);
    }

    /// <summary>
    /// The statement's <c>alg</c> and whether it carries a certificate chain or an ECDAA key;
    /// null when a key is given twice, or <c>alg</c> is not an integer.
    /// </summary>
    private static AttestationStatement? ReadStatement(ReadOnlyMemory<byte> encoded)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Lax);
        var entries = reader.ReadStartMap();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int? algorithm = null;
        var hasCertificate = false;
        for (var index = 0; entries is null ? reader.PeekState() != CborReaderState.EndMap : index < entries; index++)
        {
            if (reader.PeekState() != CborReaderState.TextString)
            {
                reader.SkipValue();
                reader.SkipValue();
                continue;
            }

            var key = reader.ReadTextString();
            if (!seen.Add(key))
            {
                return null;
            }

            switch (key)
            {
                case "x5c":
                case "ecdaaKeyId":
                    hasCertificate = true;
                    reader.SkipValue();
                    break;

                case "alg":
                    if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
                    {
                        return null;
                    }

                    algorithm = reader.ReadInt32();
                    break;

                default:
                    reader.SkipValue();
                    break;
            }
        }

        return new AttestationStatement(algorithm, hasCertificate);
    }

    /// <summary>The COSE key type of the attested credential, or null when it cannot be read.</summary>
    private static int? KeyTypeOf(byte[] authData)
    {
        if (authData.Length < AttestedCredentialDataOffset + AaguidBytes + 2
            || (authData[FlagsOffset] & AttestedCredentialDataFlag) == 0)
        {
            return null;
        }

        var lengthOffset = AttestedCredentialDataOffset + AaguidBytes;
        var credentialIdLength = BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(lengthOffset, 2));
        var keyOffset = lengthOffset + 2 + credentialIdLength;
        if (keyOffset >= authData.Length)
        {
            return null;
        }

        // The key is the first CBOR item after the credential id; extensions may follow it.
        var reader = new CborReader(authData.AsMemory(keyOffset), CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
        var entries = reader.ReadStartMap();
        int? keyType = null;
        for (var index = 0; entries is null ? reader.PeekState() != CborReaderState.EndMap : index < entries; index++)
        {
            if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
            {
                reader.SkipValue();
                reader.SkipValue();
                continue;
            }

            if (reader.ReadInt32() != CoseKeyTypeLabel)
            {
                reader.SkipValue();
                continue;
            }

            // A label given twice could read as one key type here and another in the library,
            // however each is encoded; a key type given as text is one the library may read and
            // this one does not.
            if (keyType is not null || reader.PeekState() != CborReaderState.UnsignedInteger)
            {
                return null;
            }

            keyType = reader.ReadInt32();
        }

        return keyType;
    }
}
