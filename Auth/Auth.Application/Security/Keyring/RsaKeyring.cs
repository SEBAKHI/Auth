using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// The RSA signing keyring (S12, KR1–KR4): the key that signs (current), at most one key
/// staged to sign next, previous keys still accepted until their window ends, and records of
/// compromised keys, which are neither accepted nor published. Immutable: a transition returns
/// a new ring, so a reader never sees half of a change.
/// </summary>
/// <remarks>
/// <para>
/// The same type describes the ring the secrets file holds (with the PEMs) and the ring a
/// process signs under (names, states and dates only). Both are read and changed by the same
/// rules, so the file and the process cannot apply a promote two different ways.
/// </para>
/// <para>
/// What a ring accepts (<see cref="Verifies"/>): the current key; the staged key until 30
/// days after it was published; a previous key until its <see cref="SigningKey.VerifyUntilUtc"/>;
/// never a compromised key. What it publishes (<see cref="Published"/>) is the same set, the
/// current key first, which is the order the JWKS lists and the PEM endpoint reads.
/// </para>
/// </remarks>
public sealed partial class RsaKeyring
{
    /// <summary>
    /// The kid of the key migrated from the single signing key when <c>Jwt:KeyId</c> names none:
    /// the name every deployment has published it under since the first release.
    /// </summary>
    public const string MigratedKeyId = "auth-key-1";

    /// <summary>The longest kid a key may have: a kid is a configuration key segment (<c>Keyring:Rsa:Keys:{kid}</c>) and a JWT header value.</summary>
    public const int MaxKidLength = 128;

    /// <summary>
    /// How long the staged key is published before a scheduled promote (W_publish): twice the
    /// 12 hours a JWKS client caches the key set for by default, so every application has fetched
    /// it before a token signed with it reaches them.
    /// </summary>
    public static readonly TimeSpan PublishWait = TimeSpan.FromHours(24);

    /// <summary>How long a staged key is accepted, from the moment it was published, without being promoted.</summary>
    public static readonly TimeSpan NextAcceptedFor = TimeSpan.FromDays(30);

    private readonly Dictionary<string, SigningKey> _keys;

    private RsaKeyring(
        Dictionary<string, SigningKey> keys,
        SigningKey current,
        SigningKey? next,
        IReadOnlyList<RemovedSigningKey> removed,
        bool persisted)
    {
        _keys = keys;
        Current = current;
        Next = next;
        Removed = removed;
        Persisted = persisted;
    }

    /// <summary>Every key the ring holds, compromised records included.</summary>
    public IReadOnlyCollection<SigningKey> Keys => _keys.Values.ToArray();

    /// <summary>The key that signs every token.</summary>
    public SigningKey Current { get; }

    /// <summary>The staged key, if any: accepted, published, and signing once promoted.</summary>
    public SigningKey? Next { get; }

    /// <summary>What the ring remembers of keys that left it (never trimmed).</summary>
    public IReadOnlyList<RemovedSigningKey> Removed { get; }

    /// <summary>
    /// Whether the secrets file holds this ring. False for the key migrated from the single
    /// signing key until the first keyring change saves it (KR2).
    /// </summary>
    public bool Persisted { get; }

    /// <summary>
    /// The last instant the staged key is accepted without being promoted: 30 days after it was
    /// published, or, while no process has recorded that, after it was staged. Null when there
    /// is no staged key, or the file gives it no date.
    /// </summary>
    public DateTime? NextAcceptedUntil =>
        Next is { } next && (next.PublishedAtUtc ?? next.CreatedAtUtc) is { } since ? since + NextAcceptedFor : null;

    /// <summary>
    /// When a scheduled promote of the staged key becomes possible: 24 hours after a process
    /// first published it. Null while no process has recorded that.
    /// </summary>
    public DateTime? PromoteAvailableAtUtc => Next?.PublishedAtUtc + PublishWait;

    /// <summary>
    /// The kid the single signing key is published under until the ring is first saved:
    /// <c>Jwt:KeyId</c> exactly as configured, as the single-key build published it (no trim), or
    /// <see cref="MigratedKeyId"/> when it names none.
    /// </summary>
    public static string MigratedKidFrom(string? keyId) => string.IsNullOrWhiteSpace(keyId) ? MigratedKeyId : keyId;

    /// <summary>Whether <paramref name="kid"/> can name a key of the ring: not empty, at most <see cref="MaxKidLength"/> characters, no ':'.</summary>
    public static bool IsValidKid(string? kid) =>
        !string.IsNullOrWhiteSpace(kid) && kid.Length <= MaxKidLength && !kid.Contains(':');

    /// <summary>
    /// Builds a ring and refuses one no change could have written (KR8): no key, a kid that is
    /// empty, too long or holds a ':', two keys under one kid (kids compare without case, as
    /// configuration keys do), one key under two kids, a kid that is not its key's thumbprint
    /// (the migrated key excepted), a current or staged key that is missing or compromised, or
    /// the staged key being the current one.
    /// </summary>
    public static ErrorOr<RsaKeyring> Create(
        IEnumerable<SigningKey> keys,
        string? currentKid,
        string? nextKid,
        IReadOnlyList<RemovedSigningKey>? removed,
        bool persisted = true)
    {
        var byKid = new Dictionary<string, SigningKey>(StringComparer.OrdinalIgnoreCase);
        var byThumbprint = new Dictionary<string, string>(StringComparer.Ordinal);
        var migrated = 0;

        foreach (var key in keys)
        {
            if (!IsValidKid(key.Kid))
            {
                return Inconsistent($"A signing key has the kid '{key.Kid}', which is empty, longer than {MaxKidLength} characters, or holds a ':'.");
            }

            if (!byKid.TryAdd(key.Kid, key))
            {
                return Inconsistent($"Two signing keys are named {key.Kid} (kids compare without case).");
            }

            if (!byThumbprint.TryAdd(key.Thumbprint, key.Kid))
            {
                return Inconsistent($"Signing keys {byThumbprint[key.Thumbprint]} and {key.Kid} are the same key.");
            }

            if (key.IsMigrated)
            {
                // Only the key migrated from the single signing key keeps a name that is not its
                // thumbprint, and it alone has no creation date (its age is unknown).
                if (key.CreatedAtUtc is not null || ++migrated > 1)
                {
                    return Inconsistent($"The kid {key.Kid} is not the thumbprint of its key.");
                }
            }
        }

        if (byKid.Count == 0)
        {
            return Inconsistent("The signing keyring holds no key.");
        }

        if (string.IsNullOrWhiteSpace(currentKid) || !byKid.TryGetValue(currentKid, out var current))
        {
            return Inconsistent($"The signing keyring's current key '{currentKid}' is not among its keys.");
        }

        SigningKey? next = null;
        if (!string.IsNullOrWhiteSpace(nextKid))
        {
            if (!byKid.TryGetValue(nextKid, out next))
            {
                return Inconsistent($"The signing keyring's staged key '{nextKid}' is not among its keys.");
            }

            if (ReferenceEquals(next, current))
            {
                return Inconsistent($"The signing key {current.Kid} is both the current and the staged key.");
            }
        }

        if (current.Compromised || next is { Compromised: true })
        {
            return Inconsistent("The current or the staged signing key is marked compromised.");
        }

        return new RsaKeyring(byKid, current, next, removed ?? [], persisted);
    }

    /// <summary>The key named <paramref name="kid"/>, if the ring holds it.</summary>
    public SigningKey? Find(string? kid) =>
        kid is not null && _keys.TryGetValue(kid, out var key) ? key : null;

    /// <summary>The key whose thumbprint is <paramref name="thumbprint"/>, if the ring holds it.</summary>
    public SigningKey? FindByThumbprint(string thumbprint) =>
        _keys.Values.FirstOrDefault(key => string.Equals(key.Thumbprint, thumbprint, StringComparison.Ordinal));

    /// <summary>Whether a token signed by <paramref name="key"/> is accepted at <paramref name="nowUtc"/>.</summary>
    public bool Verifies(SigningKey key, DateTime nowUtc)
    {
        if (key.Compromised)
        {
            return false;
        }

        if (IsCurrent(key))
        {
            return true;
        }

        if (IsNext(key))
        {
            return NextAcceptedUntil is not { } until || nowUtc <= until;
        }

        return key.VerifyUntilUtc is { } verifyUntil && nowUtc <= verifyUntil;
    }

    /// <summary>
    /// The keys accepted and published at <paramref name="nowUtc"/>: the current key first,
    /// then the staged key, then previous keys, the most recently demoted first.
    /// </summary>
    public IReadOnlyList<SigningKey> Published(DateTime nowUtc)
    {
        var published = new List<SigningKey> { Current };
        if (Next is { } next && Verifies(next, nowUtc))
        {
            published.Add(next);
        }

        published.AddRange(_keys.Values
            .Where(key => !IsCurrent(key) && !IsNext(key) && Verifies(key, nowUtc))
            .OrderByDescending(key => key.VerifyUntilUtc)
            .ThenBy(key => key.Kid, StringComparer.Ordinal));

        return published;
    }

    /// <summary>
    /// The same ring without its PEMs: what a process keeps once it has loaded the keys, so no
    /// private key outlives the start in a form anything could read back.
    /// </summary>
    public RsaKeyring WithoutMaterial() => new(
        _keys.Values.ToDictionary(
            key => key.Kid,
            key => key with { PublicKeyPem = null, PrivateKeyPem = null },
            StringComparer.OrdinalIgnoreCase),
        Current with { PublicKeyPem = null, PrivateKeyPem = null },
        Next is { } next ? next with { PublicKeyPem = null, PrivateKeyPem = null } : null,
        Removed,
        Persisted);

    /// <summary>The ring with the moment the staged key was first published recorded.</summary>
    public RsaKeyring WithNextPublishedAt(DateTime publishedAtUtc)
    {
        if (Next is not { } next)
        {
            return this;
        }

        var published = next with { PublishedAtUtc = publishedAtUtc };
        var keys = new Dictionary<string, SigningKey>(_keys, StringComparer.OrdinalIgnoreCase) { [next.Kid] = published };
        return new RsaKeyring(keys, Current, published, Removed, Persisted);
    }

    /// <summary>Whether <paramref name="key"/> is the current key.</summary>
    public bool IsCurrent(SigningKey key) => string.Equals(key.Kid, Current.Kid, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="key"/> is the staged key.</summary>
    public bool IsNext(SigningKey key) => Next is { } next && string.Equals(key.Kid, next.Kid, StringComparison.OrdinalIgnoreCase);

    private static Error Inconsistent(string detail) => SecretErrors.KeyringFileInconsistent(detail);
}
