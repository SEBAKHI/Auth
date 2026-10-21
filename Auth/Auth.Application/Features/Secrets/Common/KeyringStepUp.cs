using System.Collections.Frozen;
using System.Globalization;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// How a keyring change is confirmed with the step-up of the secret operations, with
/// no new <see cref="SecretOperation"/> member (KR3): the operation is the HMAC one
/// (generate, or import for a staged import), and the approval's payload digest binds
/// it to the action and to the key the action applies to.
/// </summary>
/// <remarks>
/// The target of every action but reinstate and retire is read by the server from the
/// secrets file, when the confirmation is requested and again when it is spent: a ring
/// that moved in between (another administrator promoted) gives another digest, and the
/// approval is refused. The staged key is named by its id and the moment it was staged, so
/// a key discarded and replaced under the same id is not the key that was approved. Only
/// reinstate and retire name their key, from the request.
/// </remarks>
public static class KeyringStepUp
{
    private sealed record Rule(
        SecretOperation Operation,
        Func<HmacKeyring, int?, string> Target,
        Func<HmacKeyring, int?, DateTime, ErrorOr<Success>> Precondition);

    private static readonly Func<HmacKeyring, int?, DateTime, ErrorOr<Success>> Always = (_, _, _) => Result.Success;

    private static readonly Func<HmacKeyring, int?, DateTime, ErrorOr<Success>> NeedsStagedKey =
        (ring, _, _) => ring.Next is null ? SecretErrors.KeyringNoNextKey : Result.Success;

    private static readonly FrozenDictionary<KeyringAction, Rule> Rules = new Dictionary<KeyringAction, Rule>
    {
        [KeyringAction.Stage] = new(SecretOperation.GenerateHmacKey, (ring, _) => Id(ring.Current.Id), Always),
        [KeyringAction.StageImport] = new(SecretOperation.ImportHmacKey, (ring, _) => Id(ring.Current.Id), Always),
        [KeyringAction.Promote] = new(SecretOperation.GenerateHmacKey, (ring, _) => Staged(ring), NeedsStagedKey),
        [KeyringAction.PromoteEmergency] = new(SecretOperation.GenerateHmacKey, (ring, _) => Staged(ring), Always),
        [KeyringAction.DiscardNext] = new(SecretOperation.GenerateHmacKey, (ring, _) => Staged(ring), NeedsStagedKey),
        [KeyringAction.Reinstate] = new(
            SecretOperation.GenerateHmacKey,
            (_, keyId) => Id(keyId),
            (ring, keyId, now) => keyId is { } id && ring.CanReinstate(id, now)
                ? Result.Success
                : SecretErrors.KeyringKeyNotFound(keyId ?? 0)),
        [KeyringAction.Retire] = new(
            SecretOperation.GenerateHmacKey,
            (_, keyId) => Id(keyId),
            (ring, keyId, _) => keyId is { } id && ring.CanRetire(id)
                ? Result.Success
                : SecretErrors.KeyringKeyNotFound(keyId ?? 0)),
    }.ToFrozenDictionary();

    /// <summary>The operation whose step-up confirms <paramref name="action"/>.</summary>
    public static SecretOperation OperationOf(KeyringAction action) => RuleOf(action).Operation;

    /// <summary>
    /// The key <paramref name="action"/> applies to: read from <paramref name="fileRing"/>,
    /// or <paramref name="keyId"/> for the actions that name their key. Empty when there
    /// is none (a promote with nothing staged).
    /// </summary>
    public static string TargetOf(KeyringAction action, HmacKeyring fileRing, int? keyId) =>
        RuleOf(action).Target(fileRing, keyId);

    /// <summary>
    /// Whether the ring the file holds lets <paramref name="action"/> run at all, checked
    /// before a code is emailed and again before a confirmation is spent, so neither is
    /// wasted on a change that cannot happen (a promote with nothing staged).
    /// </summary>
    public static ErrorOr<Success> Precondition(KeyringAction action, HmacKeyring fileRing, int? keyId, DateTime nowUtc) =>
        RuleOf(action).Precondition(fileRing, keyId, nowUtc);

    /// <summary>
    /// The payload digest an approval for <paramref name="action"/> on
    /// <paramref name="target"/> is bound to: <c>keyring/hmac/{action}/{target}</c>, a
    /// line break, and the imported value (empty for the others).
    /// </summary>
    public static string Digest(KeyringAction action, string target, string? value) =>
        SecretPayloadDigest.Compute($"keyring/hmac/{action}/{target}\n{value ?? string.Empty}")!;

    /// <summary>
    /// Whether <paramref name="operation"/> is the step-up operation of
    /// <paramref name="action"/>; false for an action outside the table.
    /// </summary>
    public static bool Confirms(KeyringAction action, SecretOperation operation) =>
        Rules.TryGetValue(action, out var rule) && rule.Operation == operation;

    private static Rule RuleOf(KeyringAction action) =>
        Rules.TryGetValue(action, out var rule)
            ? rule
            : throw new ArgumentOutOfRangeException(nameof(action), action, "No step-up rule for this keyring action.");

    private static string Id(int? keyId) => keyId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    // The staged key, by id and by the moment it was staged: a key discarded and another
    // staged under the same id in between is not the key that was approved.
    private static string Staged(HmacKeyring ring) =>
        ring.Next is { } next
            ? string.Concat(Id(next.Id), "@", next.CreatedAtUtc?.ToString("O", CultureInfo.InvariantCulture))
            : string.Empty;
}
