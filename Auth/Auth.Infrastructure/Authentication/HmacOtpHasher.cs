using System.Security.Cryptography;
using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Keyed-hash implementation of <see cref="IOtpHasher"/>, with a fallback that
/// still reads codes stored by the password hasher.
/// </summary>
/// <remarks>
/// The key is the platform's existing HMAC key, the one behind refresh-token and
/// invitation-token hashes. Reused rather than newly provisioned on purpose: a
/// second secret would have to be generated, protected and carried into every
/// deployment, and on the shared Windows host this runs on that is a real
/// operational cost for no cryptographic gain. What keeps the uses apart is
/// domain separation — every message hashed here carries a fixed label no other
/// caller of that key uses, so an OTP digest and a token digest computed from the
/// same key are unrelated values and neither can be replayed as the other.
/// </remarks>
public class HmacOtpHasher : IOtpHasher
{
    /// <summary>
    /// The label that separates this use of the shared key from every other one.
    /// The version segment exists so the scheme can be changed later without
    /// silently accepting values computed under the old one.
    /// </summary>
    private const string DomainLabel = "otp:v1:";

    /// <summary>
    /// Marks a value written by the password hasher. Its encoder always emits
    /// this prefix, and base64 can never begin with it, so the two forms are
    /// distinguishable with certainty rather than by guess.
    /// </summary>
    private const string LegacyArgon2Prefix = "$argon2id$";

    private readonly IRefreshTokenKeyService _keyService;
    private readonly IPasswordHasher _passwordHasher;

    public HmacOtpHasher(IRefreshTokenKeyService keyService, IPasswordHasher passwordHasher)
    {
        _keyService = keyService;
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc />
    public string Hash(string scope, string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        return _keyService.ComputeTokenHash(Message(scope, code));
    }

    /// <inheritdoc />
    public bool Verify(string scope, string code, string storedHash)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        // A code minted before this shipped. Its own expiry retires it within
        // minutes, so this branch is a deployment courtesy rather than a
        // migration — there is nothing to rewrite and nothing to backfill.
        if (storedHash.StartsWith(LegacyArgon2Prefix, StringComparison.Ordinal))
        {
            return _passwordHasher.VerifyPassword(code, storedHash);
        }

        var expected = _keyService.ComputeTokenHash(Message(scope, code));

        // Fixed-time comparison. The guess count is capped at five, so a timing
        // oracle is not the way in here — but a comparison that returns early is
        // a habit worth not having in a file about comparing secrets.
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(storedHash));
    }

    /// <summary>
    /// Label, subject and code, joined so that no two different triples can
    /// produce the same message.
    /// </summary>
    /// <remarks>
    /// The separator matters. Codes are digits and scopes are GUIDs, so a naive
    /// concatenation could not collide today — but the day a scope becomes an
    /// email address, "a@b.c" with code "12" and "a@b.c1" with code "2" would
    /// hash alike, and nothing would fail visibly.
    /// </remarks>
    private static string Message(string scope, string code) =>
        $"{DomainLabel}{scope}:{code}";
}
