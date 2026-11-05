using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.CompletePasskeyRegistration;

/// <summary>
/// Handler that completes a passkey registration (S20, PK5, PK6, PK7, PK10).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>A request that carries the challenge cookie twice is refused, and spends nothing
/// (ARR-0687).</item>
/// <item>The session rule (<see cref="PasskeyRegistrationGate"/>).</item>
/// <item>The emailed code, when the bind needs one (ARR-1049): asked for before anything is
/// spent — a request without it can never succeed — then one attempt counted against it
/// and the code checked, before the challenge is spent.</item>
/// <item>The challenge, spent by one conditional statement BEFORE the library checks the
/// response (PK6): every failure from here on spends it, so a response is never retried
/// against the same challenge.</item>
/// <item>The library's check (<see cref="IWebAuthnVerifier"/>): exact origin, RP ID, user
/// verification, ES256 or RS256. Every rejection is <c>Passkey.VerificationFailed</c>.</item>
/// <item>The row, within the limit of ten, with the emailed code spent in the same
/// transaction; a credential id registered already, to anyone, is refused.</item>
/// <item>The event, after the save: the audit row and the owner's notice (FA11).</item>
/// </list>
/// From the save on, a client that disconnects must not stop the event.
/// </remarks>
public sealed class CompletePasskeyRegistrationCommandHandler
    : IRequestHandler<CompletePasskeyRegistrationCommand, ErrorOr<PasskeyDto>>
{
    private readonly PasskeyFeature _feature;
    private readonly PasskeyRegistrationGate _gate;
    private readonly IUserRepository _userRepository;
    private readonly IUserPasskeyRepository _passkeyRepository;
    private readonly IWebAuthnChallengeRepository _challengeRepository;
    private readonly IWebAuthnVerifier _verifier;
    private readonly ICredentialHasher _credentialHasher;
    private readonly FirstFactorEmailProof _emailProof;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompletePasskeyRegistrationCommandHandler> _logger;

    public CompletePasskeyRegistrationCommandHandler(
        PasskeyFeature feature,
        PasskeyRegistrationGate gate,
        IUserRepository userRepository,
        IUserPasskeyRepository passkeyRepository,
        IWebAuthnChallengeRepository challengeRepository,
        IWebAuthnVerifier verifier,
        ICredentialHasher credentialHasher,
        FirstFactorEmailProof emailProof,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<CompletePasskeyRegistrationCommandHandler> logger)
    {
        _feature = feature;
        _gate = gate;
        _userRepository = userRepository;
        _passkeyRepository = passkeyRepository;
        _challengeRepository = challengeRepository;
        _verifier = verifier;
        _credentialHasher = credentialHasher;
        _emailProof = emailProof;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ErrorOr<PasskeyDto>> Handle(
        CompletePasskeyRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Two challenge cookies: one may be a sibling host's plant. Nothing is spent.
        if (request.ChallengeHandleRepeated || string.IsNullOrEmpty(request.ChallengeHandle))
        {
            return Refused(request.UserId, request.ChallengeHandleRepeated ? "the challenge cookie was sent twice" : "no challenge cookie");
        }

        var relyingParty = await _feature.RelyingPartyAsync(cancellationToken);
        if (relyingParty is null)
        {
            return PasskeyErrors.VerificationFailed;
        }

        // 2. The session rule.
        var allowed = await _gate.EnsureAsync(request.UserId, request.CurrentSessionId, cancellationToken);
        if (allowed.IsError)
        {
            return allowed.Errors;
        }

        // 3. The emailed code of a first qualifying factor: asked for before anything is
        //    spent, then counted and checked before the challenge is.
        Guid? bindCodeId = null;
        if (allowed.Value.EmailCodeRequired)
        {
            if (string.IsNullOrEmpty(request.EmailCode))
            {
                return TwoFactorErrors.EmailCodeRequired;
            }

            var bindCode = await _emailProof.ReserveAsync(request.UserId, request.EmailCode, cancellationToken);
            if (bindCode.IsError)
            {
                _logger.LogWarning("Invalid email code while adding a passkey for user {UserId}", request.UserId);
                return bindCode.Errors;
            }

            bindCodeId = bindCode.Value;
        }

        // 4. The challenge, spent before the response is checked.
        var optionsJson = await _challengeRepository.TryConsumeRegistrationAsync(
            _credentialHasher.Candidates(CredentialPurpose.WebAuthnChallenge, request.ChallengeHandle),
            request.UserId,
            cancellationToken);
        if (optionsJson is null)
        {
            return Refused(request.UserId, "no live registration challenge matched");
        }

        // 5. The library's check of the response against what was offered.
        var verified = await _verifier.VerifyRegistrationAsync(
            relyingParty, optionsJson, request.ResponseJson, cancellationToken);
        if (verified.IsError)
        {
            return Refused(request.UserId, "the response did not verify");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var created = UserPasskey.Create(
            request.UserId,
            verified.Value.CredentialId,
            verified.Value.PublicKey,
            verified.Value.SignCount,
            relyingParty.RpId,
            // The options required user verification and the library checked the flag
            // (UserVerificationRequirementNotMet otherwise): a verified registration is one.
            isUserVerified: true,
            verified.Value.IsBackupEligible,
            verified.Value.IsBackedUp,
            request.Name,
            now);
        if (created.IsError)
        {
            return created.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // 6. The row, within the limit, with the emailed code spent in the same transaction.
        //    From here on a client that disconnects must not stop it halfway.
        var passkey = created.Value;
        var outcome = await _passkeyRepository.TryCreateAsync(
            passkey, UserPasskey.MaxLivePerAccount, bindCodeId, CancellationToken.None);
        switch (outcome)
        {
            case PasskeyInsertOutcome.Inserted:
                break;
            case PasskeyInsertOutcome.LimitReached:
                return PasskeyErrors.LimitReached;
            case PasskeyInsertOutcome.BindCodeLost:
                return TwoFactorErrors.EmailCodeInvalid;
            default:
                return Refused(request.UserId, "the credential id is registered already");
        }

        _logger.LogInformation(
            "{EventCode}: passkey {PasskeyId} registered for user {UserId}",
            PasskeySignalCodes.Registered, passkey.Id, request.UserId);

        // 7. The audit row and the owner's notice.
        await _publisher.Publish(
            new PasskeyRegisteredEvent(
                request.UserId,
                passkey.Id,
                passkey.Name,
                request.UserId,
                user.Email.Value,
                user.NameToShow,
                allowed.Value.Session.DeviceName,
                now),
            CancellationToken.None);

        return PasskeyDto.From(passkey);
    }

    private Error Refused(Guid userId, string reason)
    {
        // The reason names the check that failed, never a value the client sent (PK16).
        _logger.LogInformation(
            "{EventCode}: a passkey registration for user {UserId} was refused: {Reason}",
            PasskeySignalCodes.VerificationFailed, userId, reason);
        return PasskeyErrors.VerificationFailed;
    }
}
