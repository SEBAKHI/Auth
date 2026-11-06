using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// A software WebAuthn authenticator for tests (S20): it makes a key pair the test owns
/// (ES256 on P-256, or RS256 on RSA-2048) and answers registration and assertion
/// ceremonies with the exact bytes a browser hands the server — a CBOR attestation object
/// with the "none" format, an authenticator-data blob, a clientDataJSON, a signature — so
/// the WebAuthn library under test checks real cryptography, not a stub.
/// </summary>
/// <remarks>
/// Built on BCL cryptography and System.Formats.Cbor only (no new dependency). Every field
/// a server checks can be bent through <see cref="CeremonyTweaks"/>, one at a time, which
/// is how the spike lists what the library raises for each kind of bad input.
/// </remarks>
internal sealed class SoftwareAuthenticator : IDisposable
{
    public const int Es256 = -7;
    public const int Rs256 = -257;

    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte BackupEligible = 0x08;
    private const byte BackedUp = 0x10;
    private const byte AttestedCredentialData = 0x40;

    private readonly ECDsa? _ec;
    private readonly RSA? _rsa;
    private uint _signCount;

    private SoftwareAuthenticator(int algorithm, byte[] credentialId)
    {
        Algorithm = algorithm;
        CredentialId = credentialId;
        if (algorithm == Es256)
        {
            _ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }
        else if (algorithm == Rs256)
        {
            _rsa = RSA.Create(2048);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "ES256 or RS256");
        }
    }

    /// <summary>The COSE algorithm of the key.</summary>
    public int Algorithm { get; }

    /// <summary>The credential id the authenticator answers with.</summary>
    public byte[] CredentialId { get; }

    /// <summary>A new authenticator with a fresh key pair and a random 32-byte credential id.</summary>
    public static SoftwareAuthenticator Create(int algorithm, byte[]? credentialId = null) =>
        new(algorithm, credentialId ?? RandomNumberGenerator.GetBytes(32));

    /// <summary>The credential's public key in COSE form, as the library stores it.</summary>
    public byte[] CosePublicKey(int? algorithmOverride = null)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        if (_ec is not null)
        {
            var parameters = _ec.ExportParameters(includePrivateParameters: false);
            writer.WriteStartMap(5);
            writer.WriteInt32(1);
            writer.WriteInt32(2); // kty: EC2
            writer.WriteInt32(3);
            writer.WriteInt32(algorithmOverride ?? Es256);
            writer.WriteInt32(-1);
            writer.WriteInt32(1); // crv: P-256
            writer.WriteInt32(-2);
            writer.WriteByteString(parameters.Q.X!);
            writer.WriteInt32(-3);
            writer.WriteByteString(parameters.Q.Y!);
            writer.WriteEndMap();
        }
        else
        {
            var parameters = _rsa!.ExportParameters(includePrivateParameters: false);
            writer.WriteStartMap(4);
            writer.WriteInt32(1);
            writer.WriteInt32(3); // kty: RSA
            writer.WriteInt32(3);
            writer.WriteInt32(algorithmOverride ?? Rs256);
            writer.WriteInt32(-1);
            writer.WriteByteString(parameters.Modulus!);
            writer.WriteInt32(-2);
            writer.WriteByteString(parameters.Exponent!);
            writer.WriteEndMap();
        }

        return writer.Encode();
    }

    /// <summary>
    /// Answers a registration: the browser's <c>PublicKeyCredential</c> as JSON, for the
    /// options JSON the server offered.
    /// </summary>
    public string Register(string optionsJson, string origin, CeremonyTweaks? tweaks = null)
    {
        tweaks ??= new CeremonyTweaks();
        using var options = JsonDocument.Parse(optionsJson);
        var challenge = options.RootElement.GetProperty("challenge").GetString()!;
        var rpId = tweaks.RpId ?? options.RootElement.GetProperty("rp").GetProperty("id").GetString()!;

        var clientData = ClientDataJson("webauthn.create", tweaks.Challenge ?? challenge, origin, tweaks);
        var authData = AuthenticatorData(rpId, tweaks, includeCredential: true);
        var attestation = AttestationObject(tweaks.AttestationFormat ?? "none", authData, tweaks);

        var credential = new JsonObject
        {
            ["id"] = Base64Url(tweaks.ResponseCredentialId ?? CredentialId),
            ["rawId"] = Base64Url(tweaks.ResponseCredentialId ?? CredentialId),
            ["type"] = tweaks.CredentialType ?? "public-key",
            ["response"] = new JsonObject
            {
                ["attestationObject"] = tweaks.RawAttestationObject ?? Base64Url(attestation),
                ["clientDataJSON"] = tweaks.RawClientDataJson ?? Base64Url(clientData),
                ["transports"] = new JsonArray("internal"),
            },
            ["clientExtensionResults"] = new JsonObject(),
        };

        return credential.ToJsonString();
    }

    /// <summary>
    /// Answers an assertion: the browser's <c>PublicKeyCredential</c> as JSON, signed by the
    /// credential's private key, for the assertion options the server offered.
    /// </summary>
    public string Assert(string optionsJson, string origin, byte[] userHandle, CeremonyTweaks? tweaks = null)
    {
        tweaks ??= new CeremonyTweaks();
        using var options = JsonDocument.Parse(optionsJson);
        var challenge = options.RootElement.GetProperty("challenge").GetString()!;
        var rpId = tweaks.RpId ?? options.RootElement.GetProperty("rpId").GetString()!;

        _signCount = tweaks.SignCount ?? _signCount + 1;
        var clientData = ClientDataJson("webauthn.get", tweaks.Challenge ?? challenge, origin, tweaks);
        var authData = AuthenticatorData(rpId, tweaks, includeCredential: false);

        var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
        var signature = Sign(signed);
        if (tweaks.CorruptSignature)
        {
            signature[^1] ^= 0xFF;
        }

        var credential = new JsonObject
        {
            ["id"] = Base64Url(tweaks.ResponseCredentialId ?? CredentialId),
            ["rawId"] = Base64Url(tweaks.ResponseCredentialId ?? CredentialId),
            ["type"] = tweaks.CredentialType ?? "public-key",
            ["response"] = new JsonObject
            {
                ["authenticatorData"] = tweaks.RawAuthenticatorData ?? Base64Url(authData),
                ["clientDataJSON"] = tweaks.RawClientDataJson ?? Base64Url(clientData),
                ["signature"] = Base64Url(signature),
                ["userHandle"] = Base64Url(userHandle),
            },
            ["clientExtensionResults"] = new JsonObject(),
        };

        return credential.ToJsonString();
    }

    public void Dispose()
    {
        _ec?.Dispose();
        _rsa?.Dispose();
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private byte[] Sign(byte[] data) =>
        _ec is not null
            ? _ec.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
            : _rsa!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    private static byte[] ClientDataJson(string type, string challenge, string origin, CeremonyTweaks tweaks)
    {
        var clientData = new JsonObject
        {
            ["type"] = tweaks.ClientDataType ?? type,
            ["challenge"] = challenge,
            ["origin"] = origin,
            ["crossOrigin"] = tweaks.CrossOrigin,
        };
        if (tweaks.TopOrigin is not null)
        {
            clientData["topOrigin"] = tweaks.TopOrigin;
        }

        return Encoding.UTF8.GetBytes(clientData.ToJsonString());
    }

    private byte[] AuthenticatorData(string rpId, CeremonyTweaks tweaks, bool includeCredential)
    {
        var flags = (byte)(UserPresent | UserVerified);
        if (!tweaks.UserVerified)
        {
            flags &= unchecked((byte)~UserVerified);
        }

        if (!tweaks.UserPresent)
        {
            flags &= unchecked((byte)~UserPresent);
        }

        if (tweaks.BackupEligible)
        {
            flags |= BackupEligible;
        }

        if (tweaks.BackedUp)
        {
            flags |= BackedUp;
        }

        if (includeCredential && !tweaks.OmitAttestedCredentialFlag)
        {
            flags |= AttestedCredentialData;
        }

        var data = new List<byte>();
        data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        data.Add(flags);
        var counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, includeCredential ? tweaks.SignCount ?? 0 : _signCount);
        data.AddRange(counter);

        if (includeCredential)
        {
            data.AddRange(new byte[16]); // AAGUID: none attestation
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
            data.AddRange(length);
            data.AddRange(CredentialId);
            data.AddRange(tweaks.CosePublicKey ?? CosePublicKey(tweaks.KeyAlgorithm));
        }

        return [.. data];
    }

    private static byte[] AttestationObject(string format, byte[] authData, CeremonyTweaks tweaks)
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString(format);
        writer.WriteTextString("attStmt");
        if (tweaks.AttestationStatement is { } statement)
        {
            statement(writer);
        }
        else
        {
            writer.WriteStartMap(0);
            writer.WriteEndMap();
        }
        writer.WriteTextString("authData");
        writer.WriteByteString(tweaks.TruncateAuthData ? authData[..20] : authData);
        writer.WriteEndMap();
        return writer.Encode();
    }
}

/// <summary>One field of a ceremony, bent: everything defaults to what a well-behaved authenticator sends.</summary>
internal sealed record CeremonyTweaks
{
    public string? Challenge { get; init; }
    public string? RpId { get; init; }
    public string? ClientDataType { get; init; }
    public bool CrossOrigin { get; init; }
    public string? TopOrigin { get; init; }
    public bool UserVerified { get; init; } = true;
    public bool UserPresent { get; init; } = true;
    public bool BackupEligible { get; init; }
    public bool BackedUp { get; init; }
    public bool OmitAttestedCredentialFlag { get; init; }
    public bool TruncateAuthData { get; init; }
    public bool CorruptSignature { get; init; }
    public uint? SignCount { get; init; }
    public int? KeyAlgorithm { get; init; }
    public byte[]? CosePublicKey { get; init; }
    public byte[]? ResponseCredentialId { get; init; }
    public string? AttestationFormat { get; init; }

    /// <summary>Writes the <c>attStmt</c> map in place of the empty one attestation <c>none</c> sends.</summary>
    public Action<CborWriter>? AttestationStatement { get; init; }
    public string? CredentialType { get; init; }
    public string? RawAttestationObject { get; init; }
    public string? RawClientDataJson { get; init; }
    public string? RawAuthenticatorData { get; init; }
}
