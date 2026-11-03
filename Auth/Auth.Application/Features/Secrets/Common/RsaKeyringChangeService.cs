using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>What a signing keyring change needs that only its command knows.</summary>
/// <param name="Action">The change, for the confirmation and the audit trail.</param>
/// <param name="Operation">The step-up operation whose confirmation is spent.</param>
/// <param name="ChallengeId">The confirmation to spend.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
/// <param name="Value">A private key the administrator supplied, which the confirmation is bound to; null otherwise.</param>
/// <param name="Kid">The key a retire names; null otherwise.</param>
/// <param name="AliasDigest">
/// True for the generate/rsa and import/rsa aliases, whose confirmations carry today's digests
/// (none for a generate, the value's for an import), so a console cached before the keyring
/// keeps working until SUNSET(S12) removes them.
/// </param>
public sealed record SigningKeyChangeRequest(
    KeyringAction Action,
    SecretOperation Operation,
    Guid ChallengeId,
    Guid RequestedBy,
    string? Value = null,
    string? Kid = null,
    bool AliasDigest = false);

/// <summary>What a signing keyring transition is computed with.</summary>
/// <param name="NowUtc">The moment of the change.</param>
/// <param name="PreviousVerifyUntilUtc">How long a key that stops signing now is still accepted (<see cref="SigningKeyOverlap"/>).</param>
/// <param name="NewKey">The key the change installs (generated or supplied), if any.</param>
/// <param name="FileBefore">The ring the file held when the change was approved.</param>
public sealed record SigningKeyChangeContext(
    DateTime NowUtc,
    DateTime PreviousVerifyUntilUtc,
    SigningKey? NewKey,
    RsaKeyring FileBefore);

/// <summary>
/// Runs one change of the RSA signing keyring, end to end (S12, KR3): refuses where the ring
/// cannot be kept, spends the step-up confirmation bound to this exact change, applies the
/// transition to the ring the secrets FILE holds under the file's lock, saves it, and then, still
/// under the lock, applies the change to this process when it only moves keys this process loaded
/// at start.
/// </summary>
/// <remarks>
/// <para>
/// A staged key enters a process only when it starts (D-T7 = A), so a stage or a discard changes
/// the file alone, and a promote moves the signer to the staged key only in a process that loaded
/// it. An emergency whose staged key this process never loaded is saved, and closes the leaked key
/// at the next restart; the answer says so (<see cref="SigningKeyringDto.ChangeAppliedInProcess"/>).
/// </para>
/// <para>
/// A transition that fails, or a save that fails, swaps nothing. The confirmation is spent first
/// and stays spent, as for every destructive secret operation. No background worker and no database
/// lock: the file's lock orders the changes (OI-217).
/// </para>
/// </remarks>
public sealed class RsaKeyringChangeService
{
    private readonly IDpapiSecretService _secretService;
    private readonly ISigningKeyRing _process;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly IPublisher _publisher;

    // Read live for the access-token lifetime only (T12); never for the migrated key's name.
    private readonly IOptionsMonitor<JwtSettings> _jwtSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RsaKeyringChangeService> _logger;

    public RsaKeyringChangeService(
        IDpapiSecretService secretService,
        ISigningKeyRing process,
        SecretOperationChallengeService challengeService,
        IPublisher publisher,
        IOptionsMonitor<JwtSettings> jwtSettings,
        TimeProvider timeProvider,
        ILogger<RsaKeyringChangeService> logger)
    {
        _secretService = secretService;
        _process = process;
        _challengeService = challengeService;
        _publisher = publisher;
        _jwtSettings = jwtSettings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The ring the secrets file holds, or why it cannot be managed here.</summary>
    public async Task<ErrorOr<RsaKeyring>> ReadFileRingAsync(CancellationToken cancellationToken)
    {
        // The source is the loader's (fact 5): a key from Jwt:PrivateKeyPath, Jwt:PrivateKeyEncrypted
        // or a PlainText settings file has no ring to change.
        if (!_process.Source.HoldsRing)
        {
            return SecretErrors.KeyringUnavailable;
        }

        try
        {
            return RsaKeyringFile.Read(await _secretService.LoadSecretsAsync(cancellationToken), MigratedKid);
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt the secrets file while reading the signing keyring");
            return SecretErrors.DecryptionFailed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to read the secrets file while reading the signing keyring");
            return SecretErrors.FileAccessFailed;
        }
    }

    /// <summary>
    /// Whether <paramref name="action"/> can run on the ring the file holds, from this process:
    /// checked before a code is emailed and again before it is spent. Refused when the file no
    /// longer holds the key this process signs with (the file was replaced behind it), and, unless
    /// the request is an alias, when the action's own precondition fails (KeyringStepUp).
    /// </summary>
    public ErrorOr<Success> Allows(
        KeyringAction action, RsaKeyring fileRing, string? kid, string? value, DateTime nowUtc, bool alias = false)
    {
        if (SignerMissingFrom(fileRing, _process.Current) is { } lost)
        {
            return FileLostKey(lost);
        }

        return alias
            ? Result.Success
            : KeyringStepUp.SigningPrecondition(action, fileRing, _process.Current, kid, value, nowUtc);
    }

    /// <summary>
    /// The signing keyring as the administrator sees it: the file's and this process's, or, where
    /// it cannot be managed, why, with the key this process signs with. Never an error: the HMAC
    /// keyring read it is part of answers either way.
    /// </summary>
    public async Task<SigningKeyringDto> ViewAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var source = _process.Source;
        if (source.UnavailableReason is { } reason)
        {
            return SigningKeyringView.Unavailable(_process.Current, source, now, reason);
        }

        var file = await ReadFileRingAsync(cancellationToken);
        return file.IsError
            ? SigningKeyringView.Unavailable(_process.Current, source, now, "Unreadable", file.FirstError.Code)
            : SigningKeyringView.Build(file.Value, _process.Current, source, now);
    }

    /// <summary>Runs one signing keyring change.</summary>
    /// <param name="request">The change.</param>
    /// <param name="newKey">The key the change installs (generated or supplied); null when it installs none.</param>
    /// <param name="apply">The transition, applied to the ring the file holds at the moment of the save.</param>
    /// <param name="applyInProcess">
    /// The same transition for this process's ring, when the change may reach it now (a promote, a
    /// retire); null when it waits for the next start (a stage, a discard, the aliases).
    /// </param>
    /// <param name="cancellationToken">The request's token; it stops nothing once the confirmation is spent and the save started.</param>
    public async Task<ErrorOr<SigningKeyringDto>> ChangeAsync(
        SigningKeyChangeRequest request,
        SigningKey? newKey,
        Func<RsaKeyring, SigningKeyChangeContext, ErrorOr<RsaKeyring>> apply,
        Func<RsaKeyring, SigningKeyChangeContext, ErrorOr<RsaKeyring>>? applyInProcess,
        CancellationToken cancellationToken)
    {
        var before = await ReadFileRingAsync(cancellationToken);
        if (before.IsError)
        {
            return before.Errors;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var target = KeyringStepUp.SigningTargetOf(request.Action, before.Value, request.Kid, request.Value);

        var possible = Allows(request.Action, before.Value, request.Kid, request.Value, now, alias: request.AliasDigest);
        if (possible.IsError)
        {
            return possible.Errors;
        }

        // A request without a live approval for this change learns nothing from the checks below.
        var digest = DigestOf(request, target);
        var approvable = await _challengeService.EnsureSpendableAsync(
            request.ChallengeId, request.Operation, digest, request.RequestedBy, cancellationToken);
        if (approvable.IsError)
        {
            return approvable.Errors;
        }

        // A key the ring holds or remembers is refused before the approval is spent, alias or not:
        // the administrator keeps the code (T6).
        if (newKey is not null && before.Value.Next?.Thumbprint != newKey.Thumbprint)
        {
            var fresh = before.Value.AcceptsNewKey(newKey.Thumbprint);
            if (fresh.IsError)
            {
                return fresh.Errors;
            }
        }

        // The window of the key a promote demotes, from the lifetime in force now (T12).
        var context = new SigningKeyChangeContext(
            now,
            SigningKeyOverlap.PreviousVerifyUntil(now, _jwtSettings.CurrentValue.AccessTokenLifetime),
            newKey,
            before.Value);

        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId, request.Operation, digest, request.RequestedBy, cancellationToken);
        if (approval.IsError)
        {
            return approval.Errors;
        }

        var appliedInProcess = false;
        ErrorOr<RsaKeyring> saved;
        try
        {
            saved = await _secretService.UpdateAsync<RsaKeyring>(
                secrets =>
                {
                    var current = RsaKeyringFile.Read(secrets, MigratedKid);
                    if (current.IsError)
                    {
                        return current.Errors;
                    }

                    // The confirmation names the key the file named when it was requested; a ring
                    // that moved since (another administrator) is not what was approved.
                    if (!request.AliasDigest &&
                        KeyringStepUp.SigningTargetOf(request.Action, current.Value, request.Kid, request.Value) != target)
                    {
                        return SecretErrors.ChallengeNotApproved;
                    }

                    if (SignerMissingFrom(current.Value, _process.Current) is { } lost)
                    {
                        return FileLostKey(lost);
                    }

                    var changed = apply(current.Value, context);
                    if (changed.IsError)
                    {
                        return changed.Errors;
                    }

                    RsaKeyringFile.Write(changed.Value, secrets);

                    // The ring exactly as the file will hold it, read back from what is saved.
                    return RsaKeyringFile.Read(secrets, MigratedKid);
                },
                // A promote, an emergency, a retire and a discard save twice, so the backup copy
                // loses what the change removed: above all the private key a promote demoted, and
                // the ring before an emergency, in which the leaked key still signs (KR3).
                overwriteBackup: request.Action is KeyringAction.Promote or KeyringAction.PromoteEmergency
                    or KeyringAction.Retire or KeyringAction.DiscardNext,
                cancellationToken,
                // Under the file lock, in the order the saves happen, and only over keys this
                // process loaded: never a key it would have to read from the file now.
                onSaved: _ => appliedInProcess = applyInProcess is not null
                    && _process.TryApply(ring => applyInProcess(ring, context)));
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt the secrets file during signing keyring {Action}", request.Action);
            return SecretErrors.DecryptionFailed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _logger.LogError(ex, "Failed to save the secrets file during signing keyring {Action}", request.Action);
            return SecretErrors.FileAccessFailed;
        }

        if (saved.IsError)
        {
            return saved.Errors;
        }

        var keyId = KeyOf(request, before.Value, saved.Value);
        _logger.LogWarning(
            "Signing keyring {Action} by {UserId} on key {Kid}: the file signs with {CurrentKid}, stages {NextKid}; " +
            "this process signs with {ProcessKid} (applied here: {AppliedInProcess})",
            request.Action, request.RequestedBy, keyId, saved.Value.Current.Kid, saved.Value.Next?.Kid,
            _process.Current.Current.Kid, appliedInProcess);

        // The change is durable from here: nothing below may answer it as failed, or an
        // administrator who reads "failed" runs it again (a second emergency).
        try
        {
            await _publisher.Publish(
                new SecretOperationExecutedEvent(
                    request.ChallengeId,
                    request.Operation,
                    request.RequestedBy,
                    request.Action,
                    Target: null,
                    KeyId: keyId),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Signing keyring {Action} was saved, but its audit record was not written (challenge {ChallengeId})",
                request.Action, request.ChallengeId);
        }

        return SigningKeyringView.Build(saved.Value, _process.Current, _process.Source, now, appliedInProcess);
    }

    /// <summary>
    /// A transition for this process that runs only while it holds the staged key the file staged
    /// when the change was approved: the key a promote or an emergency moves the signer to must be
    /// one this process loaded at start, and exactly that one.
    /// </summary>
    public static Func<RsaKeyring, SigningKeyChangeContext, ErrorOr<RsaKeyring>> WithStagedKeyLoaded(
        Func<RsaKeyring, SigningKeyChangeContext, ErrorOr<RsaKeyring>> transition) =>
        (process, change) => RsaKeyringFile.SameKey(process.Next, change.FileBefore.Next) && process.Next is not null
            ? transition(process, change)
            : SecretErrors.KeyringNoNextKey;

    // The kid the ring is published under while it is not saved: Jwt:KeyId as this process read
    // it at start, never as edited since, so the first save keeps the name the JWKS publishes.
    private string MigratedKid => _process.Source.MigratedKid;

    private static string? DigestOf(SigningKeyChangeRequest request, string target) =>
        !request.AliasDigest ? KeyringStepUp.SigningDigest(request.Action, target, request.Value)
        // Today's digests, exactly (RequestSecretOperationChallengeCommandHandler): a generate
        // carries none, an import the digest of its value.
        : request.Operation == SecretOperation.ImportRsaKey ? SecretPayloadDigest.Compute(request.Value)
        : null;

    // The key this process signs with, when the file no longer holds it: the file was replaced
    // behind the process (an older copy restored), or another process retired it. A change built
    // on such a file would drop the key for good.
    private static string? SignerMissingFrom(RsaKeyring file, RsaKeyring process) =>
        file.FindByThumbprint(process.Current.Thumbprint) is null ? process.Current.Kid : null;

    // Restart first: the usual cause is another API process that changed the ring. A copy of the
    // file is the answer only to a file replaced by mistake, and never one taken before a promote or
    // an emergency.
    private static Error FileLostKey(string kid) => SecretErrors.KeyringFileInconsistent(
        $"The secrets file no longer holds signing key {kid}, which this process signs with. If another API process " +
        "changed the signing keyring, restart this one. Only if the file itself was replaced by mistake, put back the most " +
        "recent copy taken after the last keyring change, never one from before a promote or an emergency, then restart the API.");

    // The key the change applied to, for the audit trail: the one a retire names, a key that
    // started being staged or started signing, or else the staged key it acted on.
    private static string? KeyOf(SigningKeyChangeRequest request, RsaKeyring before, RsaKeyring after) =>
        request.Kid
        ?? (after.Next is { } staged && !RsaKeyringFile.SameKey(staged, before.Next) ? staged.Kid : null)
        ?? (!RsaKeyringFile.SameKey(after.Current, before.Current) ? after.Current.Kid : null)
        ?? before.Next?.Kid;
}
