using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 identifier hasher for the tombstone registry. The key comes
/// from <c>AccountDeletion:IdentifierHmacKeyPlain</c> (auto-generated into the
/// environment's local secret layer in PlainText mode; provisioned through the
/// encrypted secrets file in Certificate/Dpapi mode). Key resolution is lazy
/// so a missing key only fails deletion operations, never unrelated requests.
/// Domain-separation prefixes keep the email and username hash spaces disjoint.
/// </summary>
public class IdentifierHasher : IIdentifierHasher
{
    private readonly Lazy<byte[]> _hmacKey;

    public IdentifierHasher(IOptions<AccountDeletionSettings> settings)
    {
        _hmacKey = new Lazy<byte[]>(() => ResolveKey(settings.Value.IdentifierHmacKeyPlain));
    }

    /// <inheritdoc />
    public string HashEmail(string email) => Hash("email:" + Normalize(email));

    /// <inheritdoc />
    public string HashUsername(string username) => Hash("username:" + Normalize(username));

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private string Hash(string value)
    {
        using var hmac = new HMACSHA256(_hmacKey.Value);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static byte[] ResolveKey(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException(
                "AccountDeletion:IdentifierHmacKeyPlain is not configured. Either enable " +
                "SecretManagement AutoGenerateKeys (PlainText mode generates it into the local " +
                "settings layer), or provision AccountDeletionIdentifierHmacKey in the encrypted " +
                "secrets file (Certificate/Dpapi mode). This key is PERMANENT: identifier " +
                "reservations and restore re-application depend on it — never rotate it.");
        }

        var key = Convert.FromBase64String(base64Key);
        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                $"The account-deletion identifier HMAC key must be at least 32 bytes (256 bits); it is {key.Length} bytes.");
        }

        return key;
    }
}
