using System.Text;
using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// A passkey of one account (S20): a WebAuthn credential its device made and keeps, bound
/// to one relying party (the RP ID). The private key never leaves the device; the server
/// holds the public key, the counter and when it was used.
/// </summary>
/// <remarks>
/// <para>
/// An independent entity with its own repository, like <see cref="UserExternalLogin"/>: the
/// <see cref="User"/> aggregate owns no collections. A passkey is never deleted while the
/// account lives: removal records when, why and by whom (<see cref="Revoke"/>), so the
/// audit trail and the notices keep what they name.
/// </para>
/// <para>
/// Every passkey is user-verified (D-T15 A): registration requires the device's own check
/// (fingerprint, face or PIN), so one passkey proves two factors on its own (M3).
/// </para>
/// </remarks>
public sealed class UserPasskey : EntityBase
{
    /// <summary>The longest credential id kept, in bytes (WebAuthn L3 §5.8.3: 1023).</summary>
    public const int MaxCredentialIdBytes = 1023;

    /// <summary>The longest name, in Unicode code points (not UTF-16 units).</summary>
    public const int MaxNameCodePoints = 64;

    /// <summary>The most live passkeys one account may hold.</summary>
    public const int MaxLivePerAccount = 10;

    /// <summary>The name a passkey takes when its owner gives none.</summary>
    public const string DefaultName = "Passkey";

    private UserPasskey()
    {
    }

    /// <summary>Constructor for Dapper mapping.</summary>
    public UserPasskey(
        Guid id,
        Guid userId,
        byte[] credentialId,
        byte[] publicKey,
        long signCount,
        string rpId,
        bool isUserVerified,
        bool isBackupEligible,
        bool isBackedUp,
        string name,
        DateTime createdAt,
        DateTime? lastUsedAt,
        DateTime? lastCounterRegressionAt,
        DateTime? revokedAt,
        string? revokedReason,
        Guid? revokedBy) : base(id)
    {
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        SignCount = signCount;
        RpId = rpId;
        IsUserVerified = isUserVerified;
        IsBackupEligible = isBackupEligible;
        IsBackedUp = isBackedUp;
        Name = name;
        CreatedAt = createdAt;
        LastUsedAt = lastUsedAt;
        LastCounterRegressionAt = lastCounterRegressionAt;
        RevokedAt = revokedAt;
        RevokedReason = revokedReason;
        RevokedBy = revokedBy;
    }

    /// <summary>Gets the account the passkey belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the credential id, compared as bytes, never as text.</summary>
    public byte[] CredentialId { get; private set; } = [];

    /// <summary>Gets the credential's COSE public key: not a secret.</summary>
    public byte[] PublicKey { get; private set; } = [];

    /// <summary>Gets the signature counter last accepted.</summary>
    public long SignCount { get; private set; }

    /// <summary>Gets the relying party the credential is bound to (<c>Passkeys:RpId</c> at registration).</summary>
    public string RpId { get; private set; } = string.Empty;

    /// <summary>Gets whether the device verified the person when it made the credential. Always true.</summary>
    public bool IsUserVerified { get; private set; }

    /// <summary>Gets the BE flag: the credential may be synced to the owner's other devices.</summary>
    public bool IsBackupEligible { get; private set; }

    /// <summary>Gets the BS flag: the credential was synced when last seen.</summary>
    public bool IsBackedUp { get; private set; }

    /// <summary>Gets the name its owner gave it, at most <see cref="MaxNameCodePoints"/> code points.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets when it was registered (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Gets when it last signed someone in (UTC), or null.</summary>
    public DateTime? LastUsedAt { get; private set; }

    /// <summary>Gets when its counter last went backwards (a copied or broken authenticator), or null.</summary>
    public DateTime? LastCounterRegressionAt { get; private set; }

    /// <summary>Gets when it stopped working (UTC), or null while it is live.</summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>Gets why it stopped working: one of <see cref="Constants.PasskeyRemovalReasons"/>.</summary>
    public string? RevokedReason { get; private set; }

    /// <summary>Gets who removed it; null when no person did (a script).</summary>
    public Guid? RevokedBy { get; private set; }

    /// <summary>Gets whether it still signs in.</summary>
    public bool IsLive => RevokedAt is null;

    /// <summary>
    /// A passkey whose registration the WebAuthn library verified (PK5, PK17).
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="credentialId">The verified credential id.</param>
    /// <param name="publicKey">The verified COSE public key.</param>
    /// <param name="signCount">The counter at registration.</param>
    /// <param name="rpId">The RP ID it is bound to.</param>
    /// <param name="isUserVerified">Whether the device verified the person: must be true.</param>
    /// <param name="isBackupEligible">The BE flag.</param>
    /// <param name="isBackedUp">The BS flag.</param>
    /// <param name="name">The owner's name for it; blank takes <see cref="DefaultName"/>.</param>
    /// <param name="createdAt">The registration time (UTC), from the caller's clock.</param>
    /// <returns>
    /// The passkey; <see cref="PasskeyErrors.VerificationFailed"/> without user verification,
    /// RP ID, key, or with a credential id empty or over <see cref="MaxCredentialIdBytes"/>;
    /// <see cref="PasskeyErrors.NameTooLong"/> for a name over <see cref="MaxNameCodePoints"/>.
    /// </returns>
    public static ErrorOr<UserPasskey> Create(
        Guid userId,
        byte[] credentialId,
        byte[] publicKey,
        long signCount,
        string rpId,
        bool isUserVerified,
        bool isBackupEligible,
        bool isBackedUp,
        string? name,
        DateTime createdAt)
    {
        if (!isUserVerified
            || credentialId is not { Length: > 0 and <= MaxCredentialIdBytes }
            || publicKey is not { Length: > 0 }
            || string.IsNullOrWhiteSpace(rpId)
            || signCount < 0)
        {
            return PasskeyErrors.VerificationFailed;
        }

        var normalized = NormalizeName(name);
        if (normalized.IsError)
        {
            return normalized.Errors;
        }

        return new UserPasskey
        {
            UserId = userId,
            CredentialId = credentialId,
            PublicKey = publicKey,
            SignCount = signCount,
            RpId = rpId,
            IsUserVerified = true,
            IsBackupEligible = isBackupEligible,
            IsBackedUp = isBackedUp,
            Name = normalized.Value,
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// The name a passkey may take: without control characters or bidi controls, trimmed,
    /// blank as <see cref="DefaultName"/>, at most <see cref="MaxNameCodePoints"/> code
    /// points. Counted in code points, never UTF-16 units: 64 emoji are 64 characters, not 128.
    /// </summary>
    /// <remarks>
    /// The name is shown back in the owner's list, in the audit and in notices that may say
    /// "if this was not you", so nothing in it may hide or reorder what it shows (REVIEW-FIXES
    /// 150 F18 (h)): control characters (Cc) and the bidi embedding, override and isolate
    /// controls (U+202A–U+202E, U+2066–U+2069) are dropped, at creation and on rename. ZWNJ and
    /// ZWJ stay: Persian is written with them, and emoji sequences need ZWJ.
    /// </remarks>
    public static ErrorOr<string> NormalizeName(string? name)
    {
        var trimmed = WithoutHiddenControls(name)?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return DefaultName;
        }

        return CountCodePoints(trimmed) > MaxNameCodePoints
            ? PasskeyErrors.NameTooLong
            : trimmed;
    }

    /// <summary>Gives the passkey another name.</summary>
    public ErrorOr<Success> Rename(string? name)
    {
        var normalized = NormalizeName(name);
        if (normalized.IsError)
        {
            return normalized.Errors;
        }

        Name = normalized.Value;
        return Result.Success;
    }

    /// <summary>
    /// Stops the passkey from signing in. A revoked one stays revoked: the first reason is
    /// the one recorded.
    /// </summary>
    /// <param name="reason">One of <see cref="Constants.PasskeyRemovalReasons"/>.</param>
    /// <param name="revokedBy">The person who removed it, or null.</param>
    /// <param name="revokedAt">When (UTC), from the caller's clock.</param>
    public void Revoke(string reason, Guid? revokedBy, DateTime revokedAt)
    {
        if (!IsLive)
        {
            return;
        }

        RevokedAt = revokedAt;
        RevokedReason = reason;
        RevokedBy = revokedBy;
    }

    /// <summary>The text without control characters and bidi embedding, override and isolate controls.</summary>
    private static string? WithoutHiddenControls(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var kept = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsControl(character)
                || character is >= '‪' and <= '‮'
                || character is >= '⁦' and <= '⁩')
            {
                continue;
            }

            kept.Append(character);
        }

        return kept.ToString();
    }

    private static int CountCodePoints(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
