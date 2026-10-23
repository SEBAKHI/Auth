using Auth.Application.Interfaces;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Enums;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Keyed-hash implementation of <see cref="IOtpHasher"/>, with a fallback that
/// still reads codes stored by the password hasher.
/// </summary>
/// <remarks>
/// The key is the platform's HMAC keyring, the one behind refresh-token and
/// invitation-token fingerprints, reached through the credential hasher under the
/// OTP purposes. Reused rather than newly provisioned on purpose: a second secret
/// would have to be generated, protected and carried into every deployment, for no
/// cryptographic gain. What keeps the uses apart is domain separation: each purpose
/// has its own subkey, and every message hashed here also carries a fixed label no
/// other caller uses, which is what separates the legacy form (key 1's raw key,
/// shared by every purpose) — an OTP digest and a token digest are unrelated values
/// and neither can be replayed as the other.
/// <para>
/// A code that proves the first factor while a second one is bound
/// (<see cref="FirstFactorEmailProof"/>) has a purpose of its own, TwoFactorBind,
/// chosen from its scope's prefix through <see cref="PurposesByScopePrefix"/>.
/// The message is the same as before, so the legacy form of both is unchanged.
/// So does the code emailed as a second factor (<see cref="EmailCodeProofStrategy"/>),
/// TwoFactorEmailCode, whose scope names the challenge it was emailed for.
/// </para>
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

    /// <summary>
    /// The scopes whose codes are a credential purpose of their own; every other
    /// scope is <see cref="CredentialPurpose.Otp"/>.
    /// </summary>
    private static readonly (string Prefix, CredentialPurpose Purpose)[] PurposesByScopePrefix =
    [
        (FirstFactorEmailProof.ScopePrefix, CredentialPurpose.TwoFactorBind),
        // The code emailed as a second factor (S24), scoped to its challenge.
        (EmailCodeProofStrategy.ScopePrefix, CredentialPurpose.TwoFactorEmailCode)
    ];

    private readonly ICredentialHasher _credentialHasher;
    private readonly IPasswordHasher _passwordHasher;

    public HmacOtpHasher(ICredentialHasher credentialHasher, IPasswordHasher passwordHasher)
    {
        _credentialHasher = credentialHasher;
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc />
    public string Hash(string scope, string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        return _credentialHasher.Hash(PurposeOf(scope), Message(scope, code));
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

        // Fixed-time comparison, inside the hasher, against every key the purpose
        // still accepts. The guess count is capped at five, so a timing oracle is not
        // the way in here — but a comparison that returns early is a habit worth not
        // having in a file about comparing secrets.
        return _credentialHasher.Verify(PurposeOf(scope), Message(scope, code), storedHash);
    }

    /// <summary>The credential purpose a code of <paramref name="scope"/> is fingerprinted under.</summary>
    internal static CredentialPurpose PurposeOf(string scope)
    {
        foreach (var (prefix, purpose) in PurposesByScopePrefix)
        {
            if (scope.StartsWith(prefix, StringComparison.Ordinal))
            {
                return purpose;
            }
        }

        return CredentialPurpose.Otp;
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
