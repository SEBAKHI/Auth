using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Text.Json;

namespace Auth.Infrastructure.Authentication.Passkeys;

/// <summary>
/// Reads the key type of the credential a registration response attests, before the WebAuthn
/// library sees the response (REVIEW 150 F9): only an EC2 or an RSA key may reach it.
/// </summary>
/// <remarks>
/// <para>
/// Fido2 4.3.2 reads an OKP (EdDSA) key through NSec, which loads the native libsodium, before
/// it refuses the key's algorithm — the spike shows it with an RFC 8032 key
/// (<c>Fido2WebAuthnSpikeTests</c>). The options offer ES256 and RS256 only, but a client
/// chooses what it sends, and D-S20-3 (a) rests on no native library ever loading. So a key
/// whose COSE <c>kty</c> is not 2 (EC2) or 3 (RSA) is refused here, and the library never
/// parses it.
/// </para>
/// <para>
/// Strict on purpose: anything this cannot read — no attested credential, a truncated
/// authenticator-data blob, a <c>kty</c> label given twice or given as text, CBOR that does
/// not parse — is refused too. A browser's response always reads; one that does not is the
/// client's fault, and refusing it costs the same <c>Passkey.VerificationFailed</c> the
/// library would have answered. Only a closed list of parse failures is caught: it reads what
/// the client sent and nothing else, so it never throws.
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

    /// <summary>
    /// Whether the response attests a credential whose public key is an EC2 or an RSA key.
    /// False for every other key type, and for anything that cannot be read.
    /// </summary>
    public static bool IsEc2OrRsa(string responseJson)
    {
        try
        {
            using var response = JsonDocument.Parse(responseJson);
            if (response.RootElement.ValueKind != JsonValueKind.Object
                || !response.RootElement.TryGetProperty("response", out var attestation)
                || attestation.ValueKind != JsonValueKind.Object
                || !attestation.TryGetProperty("attestationObject", out var encoded)
                || encoded.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var authData = AuthenticatorData(Base64Url.DecodeFromChars(encoded.GetString()));
            return authData is not null
                && KeyTypeOf(authData) is CoseKeyTypeEc2 or CoseKeyTypeRsa;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CborContentException
            or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>The single <c>authData</c> byte string of an attestation object, or null.</summary>
    private static byte[]? AuthenticatorData(byte[] attestationObject)
    {
        var reader = new CborReader(attestationObject, CborConformanceMode.Lax);
        var entries = reader.ReadStartMap();
        byte[]? authData = null;
        for (var index = 0; entries is null ? reader.PeekState() != CborReaderState.EndMap : index < entries; index++)
        {
            if (reader.PeekState() != CborReaderState.TextString)
            {
                reader.SkipValue();
                reader.SkipValue();
                continue;
            }

            if (reader.ReadTextString() != "authData")
            {
                reader.SkipValue();
                continue;
            }

            if (authData is not null)
            {
                return null;
            }

            authData = reader.ReadByteString();
        }

        return authData;
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

            // A label given twice could read as one key type here and another in the library;
            // a key type given as text is one the library may read and this one does not.
            if (keyType is not null || reader.PeekState() != CborReaderState.UnsignedInteger)
            {
                return null;
            }

            keyType = reader.ReadInt32();
        }

        return keyType;
    }
}
