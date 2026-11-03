using System.Collections.Frozen;
using System.Globalization;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// How a keyring change is confirmed with the step-up of the secret operations, with
/// no new <see cref="SecretOperation"/> member (KR3): the operation is the ring's own
/// (generate, or import for a staged import: HMAC for the HMAC keyring of S11, RSA for the
/// signing keyring of S12), and the approval's payload digest binds it to the ring, the
/// action and the key the action applies to.
/// </summary>
/// <remarks>
/// The target of every action is read by the server from the secrets file, when the
/// confirmation is requested and again when it is spent: a ring that moved in between
/// (another administrator promoted) gives another digest, and the approval is refused.
/// Reinstate and retire take their key's id from the request; every key is named by its id
/// and the moment it was created, so a key that left the ring and another that came to carry
/// its id (in a file older than OI-217, which reused ids) is not the key that was approved
/// (SEC-B8). A signing key is named by its thumbprint, which no other key can have, so its
/// <c>kid</c> alone is the target.
/// </remarks>
public static class KeyringStepUp
{
    // A precondition reads the ring the file holds and the ring this process holds.
    private delegate ErrorOr<Success> Condition(HmacKeyring fileRing, HmacKeyring processRing, int? keyId, DateTime nowUtc);

    private sealed record Rule(
        SecretOperation Operation,
        Func<HmacKeyring, int?, string> Target,
        Condition Precondition);

    private static readonly Condition Always = (_, _, _, _) => Result.Success;

    // The signing keyring's rules (S12). A precondition reads the ring the file holds, the ring
    // this process holds, the kid a retire names, and the thumbprint of a key being imported.
    private delegate ErrorOr<Success> SigningCondition(
        RsaKeyring fileRing, RsaKeyring processRing, string? kid, string? thumbprint, DateTime nowUtc);

    private sealed record SigningRule(
        SecretOperation Operation,
        Func<RsaKeyring, string?, string?, string> Target,
        SigningCondition Precondition);

    private static readonly SigningCondition SigningAlways = (_, _, _, _, _) => Result.Success;

    private static readonly SigningCondition NeedsStagedSigningKey =
        (file, _, _, _, _) => file.Next is null ? SecretErrors.KeyringNoNextKey : Result.Success;

    // A scheduled promote needs the staged key loaded in this process, which then signs with
    // it at once, and published long enough for every application to have fetched it (KR3,
    // W_publish). A staged key past its 30 days is no longer accepted, so it cannot sign either.
    // A loaded key whose publication the file does not record is promoted after the restart
    // that records it.
    private static readonly SigningCondition StagedSigningKeyReady = (file, process, _, _, now) =>
        file.Next is not { } next || !RsaKeyringFile.SameKey(next, process.Next) ? SecretErrors.KeyringNoNextKey
        : file.NextAcceptedUntil is { } acceptedUntil && now > acceptedUntil ? SecretErrors.KeyringNoNextKey
        : file.PromoteAvailableAtUtc is not { } available ? SecretErrors.KeyringNoNextKey
        : now < available ? SecretErrors.KeyringNextTooYoung(available)
        : Result.Success;

    // A staged import: the staged key itself is staged already; any other key is refused while one
    // is staged, and so is a key the ring holds or remembers.
    private static readonly SigningCondition ImportableSigningKey = (file, _, _, thumbprint, _) =>
        thumbprint is null
            ? SecretErrors.InvalidKeyMaterial("The value is not a valid RSA private key in PEM format (expected PKCS#8 or PKCS#1).")
            : file.Next is { } staged
                ? string.Equals(staged.Thumbprint, thumbprint, StringComparison.Ordinal)
                    ? Result.Success
                    : SecretErrors.KeyringKeyReferenced(staged.Kid)
                : file.AcceptsNewKey(thumbprint);

    // A key this process still signs with stays until the process restarts: another process
    // promoted, this one was never recycled, and the file would lose the key it signs with.
    private static readonly SigningCondition RetirableSigningKey = (file, process, kid, _, _) =>
        kid is null ? SecretErrors.KeyringKeyNotFound(string.Empty)
        : file.CanRetire(kid) is { IsError: true } refused ? refused.Errors
        : RsaKeyringFile.SameKey(file.Find(kid), process.Current) ? SecretErrors.KeyringKeyReferenced(kid)
        : Result.Success;

    private static readonly FrozenDictionary<KeyringAction, SigningRule> SigningRules = new Dictionary<KeyringAction, SigningRule>
    {
        [KeyringAction.Stage] = new(SecretOperation.GenerateRsaKey, (ring, _, _) => ring.Current.Kid, SigningAlways),
        [KeyringAction.StageImport] = new(SecretOperation.ImportRsaKey, (_, _, thumbprint) => thumbprint ?? string.Empty, ImportableSigningKey),
        [KeyringAction.Promote] = new(SecretOperation.GenerateRsaKey, (ring, _, _) => ring.Next?.Kid ?? string.Empty, StagedSigningKeyReady),
        [KeyringAction.PromoteEmergency] = new(SecretOperation.GenerateRsaKey, (ring, _, _) => ring.Next?.Kid ?? string.Empty, NeedsStagedSigningKey),
        [KeyringAction.DiscardNext] = new(SecretOperation.GenerateRsaKey, (ring, _, _) => ring.Next?.Kid ?? string.Empty, NeedsStagedSigningKey),
        [KeyringAction.Retire] = new(SecretOperation.GenerateRsaKey, (ring, kid, _) => ring.Find(kid)?.Kid ?? kid ?? string.Empty, RetirableSigningKey),
    }.ToFrozenDictionary();

    private static readonly Condition NeedsStagedKey =
        (file, _, _, _) => file.Next is null ? SecretErrors.KeyringNoNextKey : Result.Success;

    // A scheduled promote needs the staged key in this process too: the process that
    // promotes issues under it at once, and one that never loaded it would verify
    // nothing issued under it (KR3). An emergency installs a fresh key of its own.
    private static readonly Condition NeedsStagedKeyLoaded =
        (file, process, _, _) => file.Next is not null && HmacKeyringFile.SameKey(file.Next, process.Next)
            ? Result.Success
            : SecretErrors.KeyringNoNextKey;

    private static readonly FrozenDictionary<KeyringAction, Rule> Rules = new Dictionary<KeyringAction, Rule>
    {
        [KeyringAction.Stage] = new(SecretOperation.GenerateHmacKey, (ring, _) => Id(ring.Current.Id), Always),
        [KeyringAction.StageImport] = new(SecretOperation.ImportHmacKey, (ring, _) => Id(ring.Current.Id), Always),
        [KeyringAction.Promote] = new(SecretOperation.GenerateHmacKey, (ring, _) => Staged(ring), NeedsStagedKeyLoaded),
        [KeyringAction.PromoteEmergency] = new(SecretOperation.GenerateHmacKey, (ring, _) => Staged(ring), Always),
        [KeyringAction.DiscardNext] = new(SecretOperation.GenerateHmacKey, (ring, _) => Staged(ring), NeedsStagedKey),
        [KeyringAction.Reinstate] = new(
            SecretOperation.GenerateHmacKey,
            Named,
            (ring, _, keyId, now) => keyId is { } id && ring.CanReinstate(id, now)
                ? Result.Success
                : SecretErrors.KeyringKeyNotFound(keyId ?? 0)),
        [KeyringAction.Retire] = new(
            SecretOperation.GenerateHmacKey,
            Named,
            (ring, _, keyId, _) => keyId is { } id && ring.CanRetire(id)
                ? Result.Success
                : SecretErrors.KeyringKeyNotFound(keyId ?? 0)),
    }.ToFrozenDictionary();

    /// <summary>The operation whose step-up confirms <paramref name="action"/>.</summary>
    public static SecretOperation OperationOf(KeyringAction action) => RuleOf(action).Operation;

    /// <summary>
    /// The key <paramref name="action"/> applies to, as <paramref name="fileRing"/> names it:
    /// for the actions that name their key, the key <paramref name="keyId"/> is there. Empty
    /// when there is none (a promote with nothing staged).
    /// </summary>
    public static string TargetOf(KeyringAction action, HmacKeyring fileRing, int? keyId) =>
        RuleOf(action).Target(fileRing, keyId);

    /// <summary>
    /// Whether the ring the file holds, and the ring this process holds, let
    /// <paramref name="action"/> run at all, checked before a code is emailed and again
    /// before a confirmation is spent, so neither is wasted on a change that cannot happen
    /// (a promote with nothing staged, or with a staged key this process never loaded).
    /// </summary>
    public static ErrorOr<Success> Precondition(
        KeyringAction action, HmacKeyring fileRing, HmacKeyring processRing, int? keyId, DateTime nowUtc) =>
        RuleOf(action).Precondition(fileRing, processRing, keyId, nowUtc);

    /// <summary>
    /// The payload digest an approval for <paramref name="action"/> on
    /// <paramref name="target"/> is bound to: <c>keyring:hmac/{action}/{target}</c>, a
    /// line break, and the imported value (empty for the others). The ':' is not a base64
    /// character, so no value an import alias accepts can spell a keyring digest: an
    /// approval to stage a key can never be spent on the emergency import of another.
    /// </summary>
    public static string Digest(KeyringAction action, string target, string? value) =>
        RingDigest("hmac", action, target, value);

    /// <summary>
    /// Whether <paramref name="operation"/> is the step-up operation of
    /// <paramref name="action"/> on either ring; false for an action outside both tables.
    /// </summary>
    public static bool Confirms(KeyringAction action, SecretOperation operation) =>
        (Rules.TryGetValue(action, out var rule) && rule.Operation == operation)
        || (SigningRules.TryGetValue(action, out var signing) && signing.Operation == operation);

    /// <summary>The operation whose step-up confirms <paramref name="action"/> on the signing keyring.</summary>
    public static SecretOperation SigningOperationOf(KeyringAction action) => SigningRuleOf(action).Operation;

    /// <summary>
    /// The signing key <paramref name="action"/> applies to, as <paramref name="fileRing"/> names
    /// it: the current key for a stage, the imported key's thumbprint for a staged import, the
    /// staged key for a promote or a discard, and the key a retire names. Empty when there is none.
    /// </summary>
    /// <param name="action">The change.</param>
    /// <param name="fileRing">The ring the secrets file holds.</param>
    /// <param name="kid">The key a retire names; null otherwise.</param>
    /// <param name="value">The private key a staged import supplies; null otherwise.</param>
    public static string SigningTargetOf(KeyringAction action, RsaKeyring fileRing, string? kid, string? value) =>
        SigningRuleOf(action).Target(fileRing, kid, ThumbprintOf(value));

    /// <summary>
    /// Whether the ring the file holds, and the ring this process holds, let
    /// <paramref name="action"/> run on the signing keyring, checked before a code is emailed and
    /// again before a confirmation is spent.
    /// </summary>
    public static ErrorOr<Success> SigningPrecondition(
        KeyringAction action, RsaKeyring fileRing, RsaKeyring processRing, string? kid, string? value, DateTime nowUtc) =>
        SigningRuleOf(action).Precondition(fileRing, processRing, kid, ThumbprintOf(value), nowUtc);

    /// <summary>
    /// The payload digest an approval for <paramref name="action"/> on the signing key
    /// <paramref name="target"/> is bound to: <c>keyring:rsa/{action}/{target}</c>, a line break,
    /// and the imported value (empty for the others). Another ring's prefix than the HMAC
    /// digest's, so an approval for one ring is never spent on the other.
    /// </summary>
    public static string SigningDigest(KeyringAction action, string target, string? value) =>
        RingDigest("rsa", action, target, value);

    private static string RingDigest(string ring, KeyringAction action, string target, string? value) =>
        SecretPayloadDigest.Compute($"keyring:{ring}/{action}/{target}\n{value ?? string.Empty}")!;

    private static SigningRule SigningRuleOf(KeyringAction action) =>
        SigningRules.TryGetValue(action, out var rule)
            ? rule
            : throw new ArgumentOutOfRangeException(nameof(action), action, "No step-up rule for this signing keyring action.");

    // The thumbprint of a supplied private key, or null when there is none or it is not one.
    private static string? ThumbprintOf(string? value) =>
        value is not null && RsaKeyringFile.FromPrivateKeyPem(value) is { IsError: false } key ? key.Value.Thumbprint : null;

    private static Rule RuleOf(KeyringAction action) =>
        Rules.TryGetValue(action, out var rule)
            ? rule
            : throw new ArgumentOutOfRangeException(nameof(action), action, "No step-up rule for this keyring action.");

    private static string Id(int? keyId) => keyId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    // The staged key, by id and by the moment it was staged: a key discarded and another
    // staged under the same id in between is not the key that was approved.
    private static string Staged(HmacKeyring ring) =>
        ring.Next is { } next ? Dated(next.Id, next.CreatedAtUtc) : string.Empty;

    // The key a reinstate or a retire names, by the id the request carries and the moment the
    // file says that key was created; "{id}@" for a key with no date (key 1, from before the
    // keyring) or one the ring does not hold, which the precondition refuses anyway.
    private static string Named(HmacKeyring ring, int? keyId) =>
        keyId is { } id ? Dated(id, ring.Find(id)?.CreatedAtUtc) : string.Empty;

    private static string Dated(int keyId, DateTime? createdAtUtc) =>
        string.Concat(Id(keyId), "@", createdAtUtc?.ToString("O", CultureInfo.InvariantCulture));
}
