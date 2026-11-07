using System.Security.Cryptography;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// The steps every passkey sign-in takes before anything about its owner is decided (S20 PR B,
/// PK8): the sign-in itself and the recovery of an account waiting for deletion share them,
/// so neither copies a check the other makes.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The challenge cookie: none, or its name sent twice, is refused and spends nothing
/// (ARR-0687).</item>
/// <item>The challenge, spent by one conditional statement BEFORE anything is checked (PK6):
/// from here on every failure has spent it, so a response is never tried twice against the same
/// challenge.</item>
/// <item>The credential id and the user handle, read by this code outside the library.</item>
/// <item>The passkey: a live row of the RP ID in force with exactly that credential id, as bytes.</item>
/// <item>The user handle: present, and byte-equal to the owner's <c>Users.WebAuthnUserHandle</c>,
/// compared here in constant time, never by a library callback (WebAuthn L3 §7.2: with
/// <c>allowCredentials</c> empty the handle is what binds the response to its owner). Only the
/// owner's handle is read before the library succeeds — even for an account waiting for
/// deletion, and nothing else of it (FA6).</item>
/// <item>The library's check (<see cref="IWebAuthnVerifier.VerifyAssertionAsync"/>): exact
/// origin, the stored options' challenge and RP ID, user verification, the signature.</item>
/// <item>The counter (PK9), in this code whatever the library did, then the record: one
/// conditional statement, only while the passkey is still live and its counter is the one
/// checked (T-B4). A counter that did not grow seals the passkey and refuses; a record that
/// matched nothing — a removal, a reset or another sign-in got there first — refuses.</item>
/// </list>
/// Every refusal is <see cref="PasskeyErrors.VerificationFailed"/>, logged with
/// <see cref="PasskeySignalCodes.VerificationFailed"/> and the reason's name; no sign-in row is
/// written for it (FA4 (4-4)). No log line names a credential id, a public key, a user handle or
/// client data (PK16).
/// <para>
/// After the door wrote its session, the passkey is read once more
/// (<see cref="StillLiveAfterTheSessionAsync"/>), so a revocation that landed between the record
/// and the session ends that session.
/// </para>
/// </remarks>
public sealed class PasskeyAssertionCeremony
{
    private readonly PasskeyFeature _feature;
    private readonly IWebAuthnChallengeRepository _challengeRepository;
    private readonly IUserPasskeyRepository _passkeyRepository;
    private readonly IWebAuthnVerifier _verifier;
    private readonly ICredentialHasher _credentialHasher;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PasskeyAssertionCeremony> _logger;

    public PasskeyAssertionCeremony(
        PasskeyFeature feature,
        IWebAuthnChallengeRepository challengeRepository,
        IUserPasskeyRepository passkeyRepository,
        IWebAuthnVerifier verifier,
        ICredentialHasher credentialHasher,
        ICredentialRevocationService credentialRevocation,
        TimeProvider timeProvider,
        ILogger<PasskeyAssertionCeremony> logger)
    {
        _feature = feature;
        _challengeRepository = challengeRepository;
        _passkeyRepository = passkeyRepository;
        _verifier = verifier;
        _credentialHasher = credentialHasher;
        _credentialRevocation = credentialRevocation;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the shared steps on one sign-in response.</summary>
    /// <param name="challengeHandle">The handle the challenge cookie carried, or null.</param>
    /// <param name="challengeHandleRepeated">Whether the request carried the cookie twice.</param>
    /// <param name="responseJson">The browser's <c>PublicKeyCredential</c>, as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The passkey that signed, recorded; or <see cref="PasskeyErrors.VerificationFailed"/>.</returns>
    public async Task<ErrorOr<UserPasskey>> ProveAsync(
        string? challengeHandle,
        bool challengeHandleRepeated,
        string responseJson,
        CancellationToken cancellationToken)
    {
        // 1. The cookie: one of two may be a sibling host's plant. Nothing is spent.
        if (challengeHandleRepeated || string.IsNullOrEmpty(challengeHandle))
        {
            return Refused(challengeHandleRepeated ? "the challenge cookie was sent twice" : "no challenge cookie");
        }

        // The endpoint answers 404 while passkeys do not run; a switch-off between the two
        // reads is refused as any failed ceremony is, before anything is spent.
        var relyingParty = await _feature.RelyingPartyAsync(cancellationToken);
        if (relyingParty is null)
        {
            return Refused("passkeys do not run");
        }

        // 2. The challenge, spent before anything is checked.
        var optionsJson = await _challengeRepository.TryConsumeAssertionAsync(
            _credentialHasher.Candidates(CredentialPurpose.WebAuthnChallenge, challengeHandle),
            cancellationToken);
        if (optionsJson is null)
        {
            return Refused("no live sign-in challenge matched");
        }

        // 3. Who the response says it is, read here.
        if (!PasskeyAssertionIdentity.TryRead(responseJson, out var credentialId, out var userHandle))
        {
            return Refused("the response does not name a credential and a user handle that read");
        }

        // 4. The passkey: live, of the RP ID in force, the credential id as bytes.
        var passkey = await _passkeyRepository.GetLiveByCredentialIdAsync(credentialId, relyingParty.RpId, cancellationToken);
        if (passkey is null)
        {
            return Refused("no live passkey of the RP ID has the credential id");
        }

        // 5. The user handle: present, and the owner's, compared here. Nothing else of the
        //    owner is read before the library succeeds.
        var ownerHandle = await _passkeyRepository.GetUserHandleAsync(passkey.UserId, cancellationToken);
        if (userHandle is null
            || ownerHandle is null
            || !CryptographicOperations.FixedTimeEquals(userHandle, ownerHandle))
        {
            return Refused(userHandle is null ? "the response carries no user handle" : "the user handle is not the passkey owner's", passkey);
        }

        // 6. The library's check against what was offered and the key that was stored.
        var storedSignCount = passkey.SignCount;
        var verified = await _verifier.VerifyAssertionAsync(
            relyingParty,
            optionsJson,
            responseJson,
            new StoredPasskeyCredential(passkey.Id, passkey.PublicKey, storedSignCount),
            cancellationToken);
        if (verified.IsError)
        {
            return Refused("the response did not verify", passkey);
        }

        // 7. The counter, in this code whatever the library did (T-B3), then the record. From
        //    here on a client that disconnects must not stop a write halfway.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var counter = verified.Value.CounterRegressed
            ? passkey.RecordCounterRegression(now)
            : passkey.RecordAssertion(verified.Value.SignCount, verified.Value.IsBackupEligible, verified.Value.IsBackedUp, now);
        if (counter == PasskeyCounterCheck.Regressed)
        {
            await _passkeyRepository.SealCounterRegressionAsync(passkey.Id, now, CancellationToken.None);
            _logger.LogWarning(
                "{EventCode}: passkey {PasskeyId} of user {UserId} answered with a counter that did not grow: the passkey was sealed",
                PasskeySignalCodes.CounterRegressed, passkey.Id, passkey.UserId);
            return Refused("the signature counter did not grow", passkey);
        }

        if (!await _passkeyRepository.TryRecordAssertionAsync(passkey, storedSignCount, CancellationToken.None))
        {
            return Refused("the passkey was removed, reset or used by another sign-in meanwhile", passkey);
        }

        return passkey;
    }

    /// <summary>
    /// The passkey read again once the session it signed is written (REVIEW 151 F2). Write, then
    /// read, on both sides: the record above holds only while the passkey is live, and this read
    /// runs only after the session exists; a removal, an administrator's reset, a lock, or a
    /// password change or reset revokes the passkey and only then ends the account's sessions
    /// (again, when it ended them first). A revocation this read does not see committed after
    /// the session was written, so its sweep finds the session; one it sees, the sign-in answers
    /// itself, by ending the session it just issued, as the code exchange does (OI-155).
    /// </summary>
    /// <remarks>
    /// Nothing here takes the request's token: the session is written, and a client that leaves
    /// now must not keep it past a revocation. A failed read fails the sign-in: its tokens are
    /// then never returned, so the session it wrote is held by no one.
    /// </remarks>
    /// <param name="passkey">The passkey that signed (<see cref="ProveAsync"/>).</param>
    /// <param name="issued">The session the door issued with it.</param>
    /// <returns>
    /// True while the passkey is live. False when it was revoked meanwhile: the issued session
    /// and its single sign-on session are ended, and the door answers
    /// <see cref="PasskeyErrors.VerificationFailed"/>.
    /// </returns>
    public async Task<bool> StillLiveAfterTheSessionAsync(UserPasskey passkey, LoginResponse issued)
    {
        if (await _passkeyRepository.GetLiveByIdAsync(passkey.UserId, passkey.Id, passkey.RpId, CancellationToken.None) is not null)
        {
            return true;
        }

        if (issued.SessionId is Guid sessionId)
        {
            await _credentialRevocation.TerminateSessionAsync(
                sessionId, passkey.UserId, TokenRevocationReasons.PasskeyRevokedMeanwhile, CancellationToken.None);
        }

        // Its cookie was never sent, and the single sign-on session ends with the session.
        await _credentialRevocation.RevokeIdpSessionAsync(issued.IdpSessionToken, CancellationToken.None);

        _logger.LogWarning(
            "{EventCode}: passkey {PasskeyId} of user {UserId} was revoked while its sign-in was written; the session just issued was ended",
            PasskeySignalCodes.VerificationFailed, passkey.Id, passkey.UserId);
        return false;
    }

    private Error Refused(string reason, UserPasskey? passkey = null)
    {
        // The reason names the check that failed, never a value the client sent (PK16).
        if (passkey is null)
        {
            _logger.LogInformation(
                "{EventCode}: a passkey sign-in was refused: {Reason}",
                PasskeySignalCodes.VerificationFailed, reason);
        }
        else
        {
            _logger.LogInformation(
                "{EventCode}: a passkey sign-in with passkey {PasskeyId} of user {UserId} was refused: {Reason}",
                PasskeySignalCodes.VerificationFailed, passkey.Id, passkey.UserId, reason);
        }

        return PasskeyErrors.VerificationFailed;
    }
}
