using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// The HMAC keys behind every opaque credential's stored fingerprint (S11): the
/// key that issues, a staged next key, previous keys that still verify, and key 1,
/// whose raw material computes the legacy form every deployment stored before the
/// keyring existed.
/// </summary>
/// <remarks>
/// Immutable: a change is a new ring, swapped in whole by
/// <see cref="HmacKeyringHolder"/>, so one operation reads one consistent ring.
/// <para>
/// Issuing: the legacy form while key 1 is current, a purpose subkey of the
/// current key otherwise. Never a subkey of key 1 (RV-P4-3): a key that is a
/// key-derivation key is never also a raw HMAC key, so the cutover to subkeys can
/// only happen on a promote of a key other than 1, and a ring whose current key
/// is not key 1 must therefore carry a cutover time.
/// </para>
/// </remarks>
public sealed partial class HmacKeyring
{
    /// <summary>The key every deployment started with; the only source of the legacy form.</summary>
    public const int LegacyKeyId = 1;

    private readonly IReadOnlyDictionary<int, HmacKey> _keys;

    private HmacKeyring(
        IReadOnlyDictionary<int, HmacKey> keys,
        HmacKey current,
        HmacKey? next,
        DateTime? legacyCutoverUtc,
        DateTime? legacyVerifyUntilUtc)
    {
        _keys = keys;
        Current = current;
        Next = next;
        LegacyCutoverUtc = legacyCutoverUtc;
        LegacyVerifyUntilUtc = legacyVerifyUntilUtc;
        Issuing = current.Id == LegacyKeyId ? HmacDerivation.Legacy(current) : HmacDerivation.Subkey(current);
    }

    /// <summary>Every key, by id.</summary>
    public IReadOnlyCollection<HmacKey> Keys => _keys.Values.ToArray();

    /// <summary>The key new fingerprints are computed under.</summary>
    public HmacKey Current { get; }

    /// <summary>A staged key: verifies, never issues.</summary>
    public HmacKey? Next { get; }

    /// <summary>When issuing moved from the legacy form to purpose subkeys; null while it has not.</summary>
    public DateTime? LegacyCutoverUtc { get; }

    /// <summary>The latest instant the legacy form is accepted for any bounded purpose, when recorded.</summary>
    public DateTime? LegacyVerifyUntilUtc { get; }

    /// <summary>How new fingerprints are computed: key 1's legacy form, or the current key's subkey.</summary>
    public HmacDerivation Issuing { get; }

    /// <summary>
    /// The ring every deployment has before it ever rotated: the single key, as key 1,
    /// current, with no dates. This is what an empty keyring in the secrets file
    /// means; it is built in memory and never written back on its own.
    /// </summary>
    public static HmacKeyring FromSingleKey(ReadOnlySpan<byte> material)
    {
        var key = new HmacKey(LegacyKeyId, material);
        return new HmacKeyring(new Dictionary<int, HmacKey> { [LegacyKeyId] = key }, key, null, null, null);
    }

    /// <summary>
    /// Builds a ring, refusing one whose shape no transition can produce.
    /// </summary>
    public static ErrorOr<HmacKeyring> Create(
        IEnumerable<HmacKey> keys,
        int currentKeyId,
        int? nextKeyId,
        DateTime? legacyCutoverUtc,
        DateTime? legacyVerifyUntilUtc)
    {
        var byId = new Dictionary<int, HmacKey>();
        foreach (var key in keys)
        {
            if (!byId.TryAdd(key.Id, key))
            {
                return SecretErrors.InvalidKeyMaterial($"The HMAC keyring holds key {key.Id} twice.");
            }
        }

        if (!byId.TryGetValue(currentKeyId, out var current))
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The HMAC keyring's current key {currentKeyId} is not one of its keys.");
        }

        HmacKey? next = null;
        if (nextKeyId is { } nextId)
        {
            if (nextId == currentKeyId || nextId == LegacyKeyId || !byId.TryGetValue(nextId, out next))
            {
                return SecretErrors.InvalidKeyMaterial(
                    $"The HMAC keyring's next key {nextId} must be one of its keys, other than the current key and key {LegacyKeyId}.");
            }
        }

        if (currentKeyId != LegacyKeyId && legacyCutoverUtc is null)
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The HMAC keyring's current key is {currentKeyId} but no cutover from the legacy form is recorded; " +
                "only a promote writes a current key other than 1, and it records the cutover with it.");
        }

        // No transition issues under a key it marked leaked: an emergency promotes a
        // fresh key, and a compromised key is never reinstated (S11 SEC-2).
        if (current.Compromised)
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The HMAC keyring's current key {currentKeyId} is marked compromised; nothing may issue under it.");
        }

        // Two ids over one material would make the legacy key 1 a key-derivation key
        // under another id (RV-P4-3 by material, not only by id), and would let a
        // retire of one id silently keep the other verifying.
        if (FirstSharedMaterial(byId.Values) is { } shared)
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The HMAC keyring holds the same material under keys {shared.First} and {shared.Second}.");
        }

        return new HmacKeyring(byId, current, next, legacyCutoverUtc, legacyVerifyUntilUtc);
    }

    /// <summary>
    /// The last instant (inclusive) a credential of <paramref name="purpose"/> stored
    /// under <paramref name="key"/> is still accepted: <see cref="DateTime.MaxValue"/> for
    /// the issuing key, null when the key verifies nothing of that purpose. The same
    /// rules <see cref="Accepted"/> applies, read as a date instead of at an instant.
    /// </summary>
    public DateTime? AcceptedUntil(HmacKey key, CredentialPurpose purpose)
    {
        var overlap = CredentialPurposePolicy.For(purpose).Overlap;

        if (key.Id == Current.Id)
        {
            return DateTime.MaxValue;
        }

        if (key.Id == Next?.Id)
        {
            return overlap.NextAcceptedUntil(key);
        }

        return key.Id == LegacyKeyId
            ? overlap.LegacyAcceptedUntil(this, key)
            : key.DemotedAtUtc is null ? null : overlap.PreviousAcceptedUntil(key);
    }

    private static (int First, int Second)? FirstSharedMaterial(IEnumerable<HmacKey> keys)
    {
        var seen = new List<HmacKey>();
        foreach (var key in keys)
        {
            if (seen.FirstOrDefault(other => other.Material.SequenceEqual(key.Material)) is { } match)
            {
                return (match.Id, key.Id);
            }

            seen.Add(key);
        }

        return null;
    }

    /// <summary>The key with <paramref name="id"/>, or null.</summary>
    public HmacKey? Find(int id) => _keys.TryGetValue(id, out var key) ? key : null;

    /// <summary>
    /// Every derivation whose fingerprint a credential of <paramref name="purpose"/>
    /// may be stored under at <paramref name="nowUtc"/>, without repeats, in order:
    /// the issuing one, the staged next key, previous keys (most recently demoted
    /// first), then key 1's legacy form.
    /// </summary>
    public IReadOnlyList<HmacDerivation> Accepted(CredentialPurpose purpose, DateTime nowUtc)
    {
        var overlap = CredentialPurposePolicy.For(purpose).Overlap;
        var accepted = new List<HmacDerivation> { Issuing };

        if (Next is { } next && overlap.NextAcceptedUntil(next) is { } nextUntil && nowUtc <= nextUntil)
        {
            accepted.Add(HmacDerivation.Subkey(next));
        }

        var previousKeys = _keys.Values
            .Where(key => key.Id != Current.Id && key.Id != Next?.Id && key.Id != LegacyKeyId && key.DemotedAtUtc is not null)
            .OrderByDescending(key => key.DemotedAtUtc);

        foreach (var previous in previousKeys)
        {
            if (overlap.PreviousAcceptedUntil(previous) is { } until && nowUtc <= until)
            {
                accepted.Add(HmacDerivation.Subkey(previous));
            }
        }

        if (!Issuing.IsLegacy &&
            Find(LegacyKeyId) is { } legacyKey &&
            overlap.LegacyAcceptedUntil(this, legacyKey) is { } legacyUntil &&
            nowUtc <= legacyUntil)
        {
            accepted.Add(HmacDerivation.Legacy(legacyKey));
        }

        return accepted;
    }
}
