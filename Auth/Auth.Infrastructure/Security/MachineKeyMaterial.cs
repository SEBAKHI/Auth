using System.Buffers.Text;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Infrastructure.Security;

/// <summary>
/// ES256 machine keys (OI-128): generation, the check of a pasted public JWK, and the one stored
/// form both produce.
/// </summary>
/// <remarks>
/// The stored form is the RFC 7638 canonical JWK, <c>{"crv":"P-256","kty":"EC","x":"…","y":"…"}</c>
/// (required members only, in lexicographic order, no whitespace), and the key's <c>kid</c> is the
/// SHA-256 thumbprint of exactly those bytes. A private member never reaches storage: generation
/// builds the stored form from the public point, and a pasted JWK that carries one is refused.
/// </remarks>
public sealed class MachineKeyMaterial : IMachineKeyMaterial
{
    /// <summary>The longest pasted JWK accepted: the column's width.</summary>
    public const int MaxJwkLength = 1000;

    /// <summary>P-256 coordinates are 32 bytes each.</summary>
    private const int CoordinateLength = 32;

    /// <summary>
    /// The members a pasted public key may carry. The four required ones, plus three public,
    /// optional ones a key tool commonly writes. Anything else, and above all every private member
    /// (<c>d</c>, and RSA's <c>p</c>, <c>q</c>, <c>dp</c>, <c>dq</c>, <c>qi</c>, <c>oth</c>, or
    /// a symmetric <c>k</c>), is refused rather than dropped: a pasted private key is a leaked
    /// private key, and its owner must hear so.
    /// </summary>
    private static readonly HashSet<string> AllowedMembers = new(StringComparer.Ordinal)
    {
        "kty", "crv", "x", "y", "kid", "use", "alg"
    };

    /// <inheritdoc />
    public GeneratedMachineKey Generate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);

        var publicKey = Stored(parameters.Q.X!, parameters.Q.Y!);
        return new GeneratedMachineKey(publicKey, ecdsa.ExportPkcs8PrivateKeyPem());
    }

    /// <inheritdoc />
    public ErrorOr<MachinePublicKey> ImportPublicJwk(string jwk)
    {
        if (string.IsNullOrWhiteSpace(jwk) || jwk.Length > MaxJwkLength)
        {
            return MachineKeyErrors.InvalidPublicKey;
        }

        try
        {
            using var document = JsonDocument.Parse(jwk, new JsonDocumentOptions { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return MachineKeyErrors.InvalidPublicKey;
            }

            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in document.RootElement.EnumerateObject())
            {
                // Every member is a string, none is repeated, and none is outside the allow-list.
                if (!AllowedMembers.Contains(member.Name)
                    || member.Value.ValueKind != JsonValueKind.String
                    || !members.TryAdd(member.Name, member.Value.GetString()!))
                {
                    return MachineKeyErrors.InvalidPublicKey;
                }
            }

            if (!Is(members, "kty", "EC")
                || !Is(members, "crv", "P-256")
                || (members.ContainsKey("alg") && !Is(members, "alg", "ES256"))
                || (members.ContainsKey("use") && !Is(members, "use", "sig"))
                || !TryCoordinate(members, "x", out var x)
                || !TryCoordinate(members, "y", out var y))
            {
                return MachineKeyErrors.InvalidPublicKey;
            }

            // The point must lie on the curve. Checked here, by the curve equation, rather than
            // left to the platform: Windows refuses such a point on import, and the check must
            // not depend on which operating system the API runs on.
            if (!IsOnP256(x, y))
            {
                return MachineKeyErrors.InvalidPublicKey;
            }

            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y }
            });

            return Stored(x, y);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException
            // Windows CNG refuses a point off the curve as "parameters not valid for this
            // platform", wrapping the CryptographicException: the same refusal in other words.
            or PlatformNotSupportedException)
        {
            return MachineKeyErrors.InvalidPublicKey;
        }
    }

    /// <summary>
    /// The ECDSA public key of a stored JWK, for verifying a signature.
    /// </summary>
    /// <exception cref="FormatException">The text is not the stored form of a P-256 public key.</exception>
    internal static ECDsa PublicKeyFrom(string storedJwk)
    {
        using var document = JsonDocument.Parse(storedJwk);
        var root = document.RootElement;
        var x = Base64Url.DecodeFromChars(root.GetProperty("x").GetString());
        var y = Base64Url.DecodeFromChars(root.GetProperty("y").GetString());
        if (x.Length != CoordinateLength || y.Length != CoordinateLength
            || root.GetProperty("kty").GetString() != "EC" || root.GetProperty("crv").GetString() != "P-256")
        {
            throw new FormatException("Not the stored form of a P-256 public key.");
        }

        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        });
    }

    /// <summary>The stored form, and the thumbprint of exactly its bytes as the kid.</summary>
    private static MachinePublicKey Stored(byte[] x, byte[] y)
    {
        // RFC 7638 §3.2: the required members of an EC key, in lexicographic order, no whitespace.
        var canonical =
            $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{Base64Url.EncodeToString(x)}\",\"y\":\"{Base64Url.EncodeToString(y)}\"}}";
        var thumbprint = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return new MachinePublicKey(thumbprint, canonical);
    }

    // NIST P-256 (FIPS 186-5, SEC 2 §2.4.2): y² = x³ − 3x + b over the prime field p.
    private static readonly BigInteger P256Prime = BigInteger.Parse(
        "0FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static readonly BigInteger P256B = BigInteger.Parse(
        "05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>True when (x, y), big-endian coordinates, is a point of P-256 (not the point at infinity).</summary>
    internal static bool IsOnP256(byte[] x, byte[] y)
    {
        var bx = new BigInteger(x, isUnsigned: true, isBigEndian: true);
        var by = new BigInteger(y, isUnsigned: true, isBigEndian: true);
        if (bx >= P256Prime || by >= P256Prime)
        {
            return false;
        }

        var left = BigInteger.ModPow(by, 2, P256Prime);
        var right = (BigInteger.ModPow(bx, 3, P256Prime) - 3 * bx + P256B) % P256Prime;
        if (right.Sign < 0)
        {
            right += P256Prime;
        }

        return left == right;
    }

    private static bool Is(Dictionary<string, string> members, string name, string expected) =>
        members.TryGetValue(name, out var value) && string.Equals(value, expected, StringComparison.Ordinal);

    private static bool TryCoordinate(Dictionary<string, string> members, string name, out byte[] value)
    {
        value = [];
        if (!members.TryGetValue(name, out var text) || string.IsNullOrEmpty(text))
        {
            return false;
        }

        value = Base64Url.DecodeFromChars(text);
        return value.Length == CoordinateLength;
    }
}
