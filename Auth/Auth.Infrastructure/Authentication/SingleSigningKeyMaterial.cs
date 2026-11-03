using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// One RSA key, named by <c>Jwt:KeyId</c>: it signs every token, verifies every token, and is the
/// only key published. Immutable, so a reader never sees half of a change.
/// </summary>
/// <remarks>Does not own the <see cref="RSA"/> it is given: whoever loaded it disposes it.</remarks>
public sealed class SingleSigningKeyMaterial : ISigningKeyMaterial
{
    private readonly RsaSecurityKey[] _keys;

    public SingleSigningKeyMaterial(RSA rsa, string keyId)
    {
        var key = new RsaSecurityKey(rsa) { KeyId = keyId };
        _keys = [key];
        Current = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    /// <inheritdoc />
    public SigningCredentials Current { get; }

    /// <inheritdoc />
    public IReadOnlyList<SecurityKey> VerificationKeys(DateTimeOffset now) => _keys;

    /// <inheritdoc />
    public IReadOnlyList<RsaSecurityKey> PublishedKeys(DateTimeOffset now) => _keys;
}
