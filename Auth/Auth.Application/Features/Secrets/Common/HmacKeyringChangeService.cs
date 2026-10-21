using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// What a keyring change needs that only its command knows.
/// </summary>
/// <param name="Action">The change, for the confirmation and the audit trail.</param>
/// <param name="Operation">The step-up operation whose confirmation is spent.</param>
/// <param name="ChallengeId">The confirmation to spend.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
/// <param name="Value">Key material the administrator supplied, which the confirmation is bound to; null otherwise.</param>
/// <param name="Material">The key material the change installs (generated or supplied), base64; null when it installs none.</param>
/// <param name="KeyId">The key a reinstate or a retire names; null otherwise.</param>
/// <param name="AliasDigest">
/// True for the generate/hmac and import/hmac aliases, whose confirmations carry
/// today's digests (none for a generate, the value's for an import), so a console
/// cached before the keyring shipped keeps working (SUNSET(S11) removes them).
/// </param>
public sealed record KeyringChangeRequest(
    KeyringAction Action,
    SecretOperation Operation,
    Guid ChallengeId,
    Guid RequestedBy,
    string? Value = null,
    string? Material = null,
    int? KeyId = null,
    bool AliasDigest = false);

/// <summary>What a keyring transition is computed with.</summary>
/// <param name="NowUtc">The moment of the change.</param>
/// <param name="DemotedKeyVerifyUntilUtc">How long a key that stops issuing now keeps verifying (KR4).</param>
/// <param name="Material">The key material the change installs, if any.</param>
/// <param name="ProcessRing">The ring this process held before the change.</param>
public sealed record KeyringChangeContext(
    DateTime NowUtc,
    DateTime DemotedKeyVerifyUntilUtc,
    byte[]? Material,
    HmacKeyring ProcessRing);

/// <summary>
/// Runs one change of the HMAC keyring, end to end (S11, KR1, KR3): refuses where the
/// ring cannot be kept, spends the step-up confirmation bound to this exact change,
/// applies the transition to the ring the secrets FILE holds under the file's lock,
/// saves it, and only then swaps it into this process, so the process issues and
/// verifies under what it just saved, with no restart.
/// </summary>
/// <remarks>
/// A transition that fails, or a save that fails, swaps nothing: the process keeps the
/// ring the file still holds. The confirmation is spent first and stays spent, as for
/// every destructive secret operation.
/// </remarks>
public sealed class HmacKeyringChangeService
{
    private readonly IDpapiSecretService _secretService;
    private readonly HmacKeyringHolder _holder;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly ISecretRotationImpactRepository _impactRepository;
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly IPublisher _publisher;
    private readonly ResolvedSecretStorageMode _storageMode;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HmacKeyringChangeService> _logger;

    public HmacKeyringChangeService(
        IDpapiSecretService secretService,
        HmacKeyringHolder holder,
        SecretOperationChallengeService challengeService,
        ISecretRotationImpactRepository impactRepository,
        IWebhookKeyRepository webhookKeyRepository,
        IPublisher publisher,
        ResolvedSecretStorageMode storageMode,
        TimeProvider timeProvider,
        ILogger<HmacKeyringChangeService> logger)
    {
        _secretService = secretService;
        _holder = holder;
        _challengeService = challengeService;
        _impactRepository = impactRepository;
        _webhookKeyRepository = webhookKeyRepository;
        _publisher = publisher;
        _storageMode = storageMode;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The ring the secrets file holds, or why it cannot be managed here.</summary>
    public async Task<ErrorOr<HmacKeyring>> ReadFileRingAsync(CancellationToken cancellationToken)
    {
        if (_storageMode.IsPlainText)
        {
            return SecretErrors.KeyringUnavailable;
        }

        try
        {
            return HmacKeyringFile.Read(await _secretService.LoadSecretsAsync(cancellationToken));
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt the secrets file while reading the HMAC keyring");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read the secrets file while reading the HMAC keyring");
            return SecretErrors.FileAccessFailed;
        }
    }

    /// <summary>The keyring as the administrator sees it: the file's and this process's.</summary>
    public async Task<ErrorOr<HmacKeyringDto>> ViewAsync(CancellationToken cancellationToken)
    {
        var file = await ReadFileRingAsync(cancellationToken);
        if (file.IsError)
        {
            return file.Errors;
        }

        var webhookKeys = await _webhookKeyRepository.CountLiveByHmacKeyAsync(cancellationToken);
        return HmacKeyringView.Build(file.Value, _holder.Current, webhookKeys);
    }

    /// <summary>Runs one keyring change.</summary>
    /// <param name="request">The change.</param>
    /// <param name="apply">The transition, applied to the ring the file holds at the moment of the save.</param>
    /// <param name="precheck">
    /// A refusal decided before the confirmation is spent (a retire that would strand
    /// webhook keys), from the ring the file holds; null when there is none.
    /// </param>
    /// <param name="cancellationToken">The request's token; it stops nothing once the confirmation is spent and the save started.</param>
    public async Task<ErrorOr<HmacKeyringDto>> ChangeAsync(
        KeyringChangeRequest request,
        Func<HmacKeyring, KeyringChangeContext, ErrorOr<HmacKeyring>> apply,
        Func<HmacKeyring, DateTime, CancellationToken, Task<ErrorOr<Success>>>? precheck,
        CancellationToken cancellationToken)
    {
        var before = await ReadFileRingAsync(cancellationToken);
        if (before.IsError)
        {
            return before.Errors;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var target = KeyringStepUp.TargetOf(request.Action, before.Value, request.KeyId);

        if (!request.AliasDigest)
        {
            var possible = KeyringStepUp.Precondition(request.Action, before.Value, request.KeyId, now);
            if (possible.IsError)
            {
                return possible.Errors;
            }
        }

        // A supplied key the ring already holds is refused here, before the approval is
        // spent, rather than by the transition after it: the administrator keeps the code.
        if (request.Material is { } material)
        {
            var fresh = before.Value.AcceptsNewMaterial(Convert.FromBase64String(material));
            if (fresh.IsError)
            {
                return fresh.Errors;
            }
        }

        if (precheck is not null)
        {
            var allowed = await precheck(before.Value, now, cancellationToken);
            if (allowed.IsError)
            {
                return allowed.Errors;
            }
        }

        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId,
            request.Operation,
            DigestOf(request, target),
            request.RequestedBy,
            cancellationToken);
        if (approval.IsError)
        {
            return approval.Errors;
        }

        var verifyUntil = await DemotedKeyVerifyUntilAsync(request.Action, now, cancellationToken);
        var context = new KeyringChangeContext(
            now,
            verifyUntil,
            request.Material is null ? null : Convert.FromBase64String(request.Material),
            _holder.Current);

        ErrorOr<HmacKeyring> saved;
        try
        {
            saved = await _secretService.UpdateAsync<HmacKeyring>(
                secrets =>
                {
                    var current = HmacKeyringFile.Read(secrets);
                    if (current.IsError)
                    {
                        return current.Errors;
                    }

                    // The confirmation names the key the file named when it was requested;
                    // a ring that moved since (another administrator) is not what was approved.
                    if (!request.AliasDigest &&
                        KeyringStepUp.TargetOf(request.Action, current.Value, request.KeyId) != target)
                    {
                        return SecretErrors.ChallengeNotApproved;
                    }

                    var changed = apply(current.Value, context);
                    if (changed.IsError)
                    {
                        return changed.Errors;
                    }

                    HmacKeyringFile.Write(changed.Value, secrets);

                    // The ring exactly as the file will hold it, read back from what is saved.
                    return HmacKeyringFile.Read(secrets);
                },
                overwriteBackup: request.Action == KeyringAction.Retire,
                cancellationToken);
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt the secrets file during HMAC keyring {Action}", request.Action);
            return SecretErrors.DecryptionFailed;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException)
        {
            _logger.LogError(ex, "Failed to save the secrets file during HMAC keyring {Action}", request.Action);
            return SecretErrors.FileAccessFailed;
        }

        if (saved.IsError)
        {
            return saved.Errors;
        }

        // Only now, with the file saved: a ring the file does not hold would be lost at
        // the next start, and the process would have issued under it meanwhile.
        _holder.Replace(saved.Value);

        _logger.LogWarning(
            "HMAC keyring {Action} by {UserId}: key {CurrentKeyId} issues, key {NextKeyId} is staged, cutover {CutoverUtc:u}",
            request.Action, request.RequestedBy, saved.Value.Current.Id, saved.Value.Next?.Id, saved.Value.LegacyCutoverUtc);

        // The save is durable: a client that disconnected must not cancel its record.
        await _publisher.Publish(
            new SecretOperationExecutedEvent(
                request.ChallengeId,
                request.Operation,
                request.RequestedBy,
                request.Action,
                TargetKeyOf(request, before.Value, saved.Value)),
            CancellationToken.None);

        var webhookKeys = await _webhookKeyRepository.CountLiveByHmacKeyAsync(CancellationToken.None);
        return HmacKeyringView.Build(saved.Value, _holder.Current, webhookKeys);
    }

    private static string? DigestOf(KeyringChangeRequest request, string target) =>
        !request.AliasDigest ? KeyringStepUp.Digest(request.Action, target, request.Value)
        // Today's digests, exactly (RequestSecretOperationChallengeCommandHandler):
        // a generate carries none, an import the digest of its value.
        : request.Operation == SecretOperation.ImportHmacKey ? SecretPayloadDigest.Compute(request.Value)
        : null;

    // A promote and a reinstate stop the current key issuing: how long it keeps verifying
    // comes from the latest live refresh token and SSO session, read now, before the save.
    private async Task<DateTime> DemotedKeyVerifyUntilAsync(KeyringAction action, DateTime now, CancellationToken cancellationToken)
    {
        if (action is not (KeyringAction.Promote or KeyringAction.Reinstate))
        {
            return now;
        }

        var snapshot = await _impactRepository.GetImpactAsync(
            TimeSpan.Zero, issuedBefore: null, PurposeLimitedOverlap.LegacyCap, cancellationToken);

        return PurposeLimitedOverlap.DemotedKeyVerifyUntil(
            now,
            AsUtc(snapshot.LatestRefreshTokenExpiresAt),
            AsUtc(snapshot.LatestSsoSessionExpiresAt));
    }

    // The key the change applied to, for the audit trail: the one it names, or the one
    // that started or stopped issuing or being staged.
    private static int? TargetKeyOf(KeyringChangeRequest request, HmacKeyring before, HmacKeyring after) =>
        request.KeyId
        ?? (after.Current.Id != before.Current.Id ? after.Current.Id
            : after.Next?.Id != before.Next?.Id ? after.Next?.Id ?? before.Next?.Id
            : null);

    // The database stores UTC without a kind.
    private static DateTime? AsUtc(DateTime? value) =>
        value is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;
}
