using System.Globalization;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Security;

/// <summary>
/// The HMAC keyring's posture (S11, KR7), from the ring the secrets FILE holds, the ring
/// this process holds, and the live webhook keys, never from a setting:
/// <list type="bullet">
/// <item><c>keyring.legacy-derivation</c>: no rotation yet (Information); after the
/// cutover, the days the legacy form is still accepted (Warning); Critical when the file
/// and this process disagree on the cutover (a save that did not reach the process, or
/// another worker).</item>
/// <item><c>keyring.previous-past-window</c>: previous keys that verify nothing any more.</item>
/// <item><c>keyring.webhook-keys-on-old-key</c>: live webhook keys stored under a key that
/// no longer issues; Critical when that key is compromised.</item>
/// <item><c>keyring.next-staged-too-long</c>: a key staged more than 7 days ago.</item>
/// <item><c>keyring.key-age</c>: a current key older than 365 days (never key 1 when its
/// age is unknown).</item>
/// </list>
/// </summary>
/// <remarks>
/// Re-evaluated at startup and after every configuration reload, so its day counts move
/// at those moments only; the periodic re-statement is S16's.
/// </remarks>
public sealed class KeyringPostureContributor : ISecurityPostureContributor
{
    internal static readonly TimeSpan StagedTooLongAfter = TimeSpan.FromDays(7);
    internal static readonly TimeSpan KeyAgeLimit = TimeSpan.FromDays(365);

    private const string PlainTextTemplate =
        "keyring: secrets are stored in {StorageMode} mode, so the HMAC keyring cannot be rotated on this deployment";
    private const string UnreadableTemplate =
        "keyring: the HMAC keyring in the secrets file cannot be read ({Reason}); the next start will refuse it";
    private const string NoRotationTemplate =
        "keyring.legacy-derivation: no rotation yet; every fingerprint is issued under key 1's legacy form";
    private const string CutoverDisagreesTemplate =
        "keyring.legacy-derivation: the secrets file records the cutover at {FileCutover} but this process at {ProcessCutover}; " +
        "a keyring change did not reach this process, or another worker made it. Restart the API";
    private const string LegacyReinstatedTemplate =
        "keyring.legacy-derivation: key 1 was reinstated, so the legacy form issues again";
    private const string LegacyOpenTemplate =
        "keyring.legacy-derivation: the legacy form is still accepted until {LegacyUntil:u} ({DaysLeft} days left), and for webhook keys until {WebhookUntil:u}";
    private const string LegacyClosedTemplate =
        "keyring.legacy-derivation: the legacy form is no longer accepted except for webhook keys until {WebhookUntil:u} (cutover {Cutover:u})";
    private const string PastWindowTemplate =
        "keyring.previous-past-window: HMAC keys {KeyIds} verify nothing any more; retire them";
    private const string OldWebhookKeysTemplate =
        "keyring.webhook-keys-on-old-key: {Count} live webhook keys are stored under older HMAC keys {KeyIds}; each is re-fingerprinted on its next use";
    private const string CompromisedWebhookKeysTemplate =
        "keyring.webhook-keys-on-old-key: {Count} live webhook keys are stored under compromised HMAC keys {KeyIds}; rotate them before their grace ends";
    private const string StagedTooLongTemplate =
        "keyring.next-staged-too-long: HMAC key {KeyId} has been staged since {StagedAt:u}; promote it or discard it";
    private const string KeyAgeTemplate =
        "keyring.key-age: the current HMAC key {KeyId} is {AgeDays} days old; rotate it";

    private readonly IDpapiSecretService _secretService;
    private readonly HmacKeyringHolder _holder;
    private readonly ResolvedSecretStorageMode _storageMode;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public KeyringPostureContributor(
        IDpapiSecretService secretService,
        HmacKeyringHolder holder,
        ResolvedSecretStorageMode storageMode,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider)
    {
        _secretService = secretService;
        _holder = holder;
        _storageMode = storageMode;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Name => "keyring";

    /// <inheritdoc />
    public async Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken)
    {
        if (_storageMode.IsPlainText)
        {
            return Posture([new Item("keyring", LogLevel.Information, PlainTextTemplate, [_storageMode.Name], "plaintext")]);
        }

        var file = HmacKeyringFile.Read(await _secretService.LoadSecretsAsync(cancellationToken));
        if (file.IsError)
        {
            return Posture([new Item("keyring", LogLevel.Critical, UnreadableTemplate, [file.FirstError.Code], "unreadable:" + file.FirstError.Code)]);
        }

        IReadOnlyDictionary<int, int> webhookKeys;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            webhookKeys = await scope.ServiceProvider.GetRequiredService<IWebhookKeyRepository>()
                .CountLiveByHmacKeyAsync(cancellationToken);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var ring = file.Value;

        var items = new List<Item> { LegacyDerivation(ring, _holder.Current, now) };
        items.AddRange(PreviousPastWindow(ring, now));
        items.AddRange(WebhookKeysOnOldKey(ring, webhookKeys));
        items.AddRange(NextStagedTooLong(ring, now));
        items.AddRange(KeyAge(ring, now));

        return Posture(items);
    }

    private static Item LegacyDerivation(HmacKeyring file, HmacKeyring process, DateTime now)
    {
        const string name = "keyring.legacy-derivation";

        if (file.LegacyCutoverUtc != process.LegacyCutoverUtc)
        {
            return new Item(name, LogLevel.Critical, CutoverDisagreesTemplate,
                [Format(file.LegacyCutoverUtc), Format(process.LegacyCutoverUtc)],
                $"disagree:{Format(file.LegacyCutoverUtc)}:{Format(process.LegacyCutoverUtc)}");
        }

        if (file.LegacyCutoverUtc is not { } cutover)
        {
            return new Item(name, LogLevel.Information, NoRotationTemplate, [], "none");
        }

        if (file.Current.Id == HmacKeyring.LegacyKeyId)
        {
            return new Item(name, LogLevel.Warning, LegacyReinstatedTemplate, [], "reinstated");
        }

        var key1 = file.Find(HmacKeyring.LegacyKeyId);
        var legacyUntil = key1 is null ? null : file.BoundedVerifyUntil(key1);
        var webhookUntil = key1 is null ? null : file.AcceptedUntil(key1, CredentialPurpose.WebhookKey);

        if (legacyUntil is { } until && until >= now)
        {
            var daysLeft = (int)Math.Floor((until - now).TotalDays);
            return new Item(name, LogLevel.Warning, LegacyOpenTemplate, [until, daysLeft, webhookUntil], $"open:{daysLeft}");
        }

        return new Item(name, LogLevel.Information, LegacyClosedTemplate, [webhookUntil, cutover], "closed");
    }

    private static IEnumerable<Item> PreviousPastWindow(HmacKeyring ring, DateTime now)
    {
        var spent = ring.Keys
            .Where(key => key.Id != ring.Current.Id && key.Id != ring.Next?.Id)
            .Where(key => !(ring.BoundedVerifyUntil(key) >= now) && !(ring.AcceptedUntil(key, CredentialPurpose.WebhookKey) >= now))
            .Select(key => key.Id)
            .OrderBy(id => id)
            .ToList();

        if (spent.Count > 0)
        {
            yield return new Item("keyring.previous-past-window", LogLevel.Warning, PastWindowTemplate,
                [string.Join(", ", spent)], "past:" + string.Join(",", spent));
        }
    }

    private static IEnumerable<Item> WebhookKeysOnOldKey(HmacKeyring ring, IReadOnlyDictionary<int, int> webhookKeys)
    {
        var old = webhookKeys
            .Where(entry => entry.Key != ring.Current.Id && entry.Value > 0)
            .OrderBy(entry => entry.Key)
            .ToList();

        if (old.Count == 0)
        {
            yield break;
        }

        var count = old.Sum(entry => entry.Value);
        var ids = string.Join(", ", old.Select(entry => entry.Key));

        // A key the ring no longer holds is as unusable as a compromised one: nothing
        // stored under it validates.
        var compromised = old.Any(entry => ring.Find(entry.Key) is not { Compromised: false });
        yield return compromised
            ? new Item("keyring.webhook-keys-on-old-key", LogLevel.Critical, CompromisedWebhookKeysTemplate, [count, ids], $"compromised:{count}:{ids}")
            : new Item("keyring.webhook-keys-on-old-key", LogLevel.Warning, OldWebhookKeysTemplate, [count, ids], $"old:{count}:{ids}");
    }

    private static IEnumerable<Item> NextStagedTooLong(HmacKeyring ring, DateTime now)
    {
        if (ring.Next is { CreatedAtUtc: { } stagedAt } next && now - stagedAt > StagedTooLongAfter)
        {
            yield return new Item("keyring.next-staged-too-long", LogLevel.Warning, StagedTooLongTemplate,
                [next.Id, stagedAt], $"staged:{next.Id}");
        }
    }

    private static IEnumerable<Item> KeyAge(HmacKeyring ring, DateTime now)
    {
        // Key 1 migrated from the single key has no CreatedAtUtc: its age is unknown, and
        // a warning on a guess would only teach the administrator to ignore this line.
        if (ring.Current.CreatedAtUtc is { } createdAt && now - createdAt > KeyAgeLimit)
        {
            var ageDays = (int)Math.Floor((now - createdAt).TotalDays);
            yield return new Item("keyring.key-age", LogLevel.Warning, KeyAgeTemplate,
                [ring.Current.Id, ageDays], $"age:{ring.Current.Id}:{ageDays}");
        }
    }

    private static SecurityPosture Posture(IReadOnlyList<Item> items)
    {
        var reasons = items.Where(item => item.Level >= LogLevel.Warning).Select(item => item.Name).ToList();
        return new SecurityPosture(
            reasons.Count > 0 ? SecurityPostureLevel.Weak : SecurityPostureLevel.Ok,
            reasons,
            items.Select(item => new SecurityPostureLine(item.Level, item.Template, item.Arguments)).ToList(),
            string.Join("|", items.Select(item => $"{item.Name}={item.Level}:{item.State}")));
    }

    private static string Format(DateTime? value) =>
        value?.ToString("u", CultureInfo.InvariantCulture) ?? "none";

    private sealed record Item(
        string Name,
        LogLevel Level,
        string Template,
        IReadOnlyList<object?> Arguments,
        string State);
}
