using System.Security.Cryptography;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.BeginPasskeyRegistration;

/// <summary>
/// Handler that starts a passkey registration (S20, PK5, PK6, PK7).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The session rule (<see cref="PasskeyRegistrationGate"/>), before anything is read
/// about the account's passkeys.</item>
/// <item>At most <see cref="UserPasskey.MaxLivePerAccount"/> live passkeys: the eleventh is
/// refused here, before the device is asked, and again when the row is written.</item>
/// <item>The account's WebAuthn user handle: 64 random bytes, set once by the first
/// registration (D-T20 A), never the account id.</item>
/// <item>The options: a discoverable, user-verified credential, attestation none, the
/// account's live credentials excluded, ES256 and RS256 only.</item>
/// <item>The challenge, stored under the keyed fingerprint of a random handle; the handle
/// goes to the browser in a cookie, and only once the row exists.</item>
/// </list>
/// </remarks>
public sealed class BeginPasskeyRegistrationCommandHandler
    : IRequestHandler<BeginPasskeyRegistrationCommand, ErrorOr<PasskeyRegistrationStarted>>
{
    /// <summary>The size of the WebAuthn user handle (D-T20 A, WebAuthn L3 §5.4.3: at most 64).</summary>
    public const int UserHandleBytes = 64;

    private readonly PasskeyFeature _feature;
    private readonly PasskeyRegistrationGate _gate;
    private readonly IUserRepository _userRepository;
    private readonly IUserPasskeyRepository _passkeyRepository;
    private readonly IWebAuthnChallengeRepository _challengeRepository;
    private readonly IWebAuthnVerifier _verifier;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ICredentialHasher _credentialHasher;
    private readonly TimeProvider _timeProvider;

    public BeginPasskeyRegistrationCommandHandler(
        PasskeyFeature feature,
        PasskeyRegistrationGate gate,
        IUserRepository userRepository,
        IUserPasskeyRepository passkeyRepository,
        IWebAuthnChallengeRepository challengeRepository,
        IWebAuthnVerifier verifier,
        ISecureTokenGenerator tokenGenerator,
        ICredentialHasher credentialHasher,
        TimeProvider timeProvider)
    {
        _feature = feature;
        _gate = gate;
        _userRepository = userRepository;
        _passkeyRepository = passkeyRepository;
        _challengeRepository = challengeRepository;
        _verifier = verifier;
        _tokenGenerator = tokenGenerator;
        _credentialHasher = credentialHasher;
        _timeProvider = timeProvider;
    }

    public async Task<ErrorOr<PasskeyRegistrationStarted>> Handle(
        BeginPasskeyRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        // The endpoint answers 404 while passkeys are off; a switch-off between the two
        // reads is refused as any failed ceremony is.
        var relyingParty = await _feature.RelyingPartyAsync(cancellationToken);
        if (relyingParty is null)
        {
            return PasskeyErrors.VerificationFailed;
        }

        var allowed = await _gate.EnsureAsync(request.UserId, request.CurrentSessionId, cancellationToken);
        if (allowed.IsError)
        {
            return allowed.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var live = await _passkeyRepository.GetLiveAsync(request.UserId, relyingParty.RpId, cancellationToken);
        if (live.Count >= UserPasskey.MaxLivePerAccount)
        {
            return PasskeyErrors.LimitReached;
        }

        var userHandle = await _passkeyRepository.EnsureUserHandleAsync(
            request.UserId, RandomNumberGenerator.GetBytes(UserHandleBytes), cancellationToken);
        if (userHandle is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var optionsJson = _verifier.CreateRegistrationOptions(
            relyingParty,
            new PasskeyRegistrationSubject(userHandle, user.Email.Value, user.NameToShow),
            live.Select(passkey => passkey.CredentialId).ToList());

        var handle = _tokenGenerator.Generate();
        var challenge = WebAuthnChallenge.IssueForRegistration(
            request.UserId,
            _credentialHasher.Hash(CredentialPurpose.WebAuthnChallenge, handle),
            optionsJson,
            _timeProvider.GetUtcNow().UtcDateTime);
        await _challengeRepository.CreateAsync(challenge, cancellationToken);

        return new PasskeyRegistrationStarted(
            optionsJson, handle, challenge.ExpiresAt, allowed.Value.EmailCodeRequired);
    }
}
