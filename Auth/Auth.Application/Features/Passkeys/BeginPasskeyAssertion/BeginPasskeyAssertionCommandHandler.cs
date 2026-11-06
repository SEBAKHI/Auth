using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.BeginPasskeyAssertion;

/// <summary>
/// Handler that starts a passkey sign-in (S20 PR B, PK5, PK6, T-B2).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>One cookie naming a challenge that is still unspent and has more than
/// <see cref="WebAuthnChallenge.ReuseMargin"/> left by the database's clock: the same options
/// and expiry again, no new row, the cookie as it is. A sign-in page fetches its options on
/// every load, and this keeps a reload from writing a row each time.</item>
/// <item>Otherwise — no cookie, the cookie sent twice, or a challenge spent, expired or about
/// to expire — new options (no credential named, user verification required), a new challenge
/// under the keyed fingerprint of a new random handle, and the handle for a new cookie.</item>
/// </list>
/// Anonymous: the options name no account, and nothing about any account is read.
/// </remarks>
public sealed class BeginPasskeyAssertionCommandHandler
    : IRequestHandler<BeginPasskeyAssertionCommand, ErrorOr<PasskeyAssertionStarted>>
{
    private readonly PasskeyFeature _feature;
    private readonly IWebAuthnChallengeRepository _challengeRepository;
    private readonly IWebAuthnVerifier _verifier;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ICredentialHasher _credentialHasher;
    private readonly TimeProvider _timeProvider;

    public BeginPasskeyAssertionCommandHandler(
        PasskeyFeature feature,
        IWebAuthnChallengeRepository challengeRepository,
        IWebAuthnVerifier verifier,
        ISecureTokenGenerator tokenGenerator,
        ICredentialHasher credentialHasher,
        TimeProvider timeProvider)
    {
        _feature = feature;
        _challengeRepository = challengeRepository;
        _verifier = verifier;
        _tokenGenerator = tokenGenerator;
        _credentialHasher = credentialHasher;
        _timeProvider = timeProvider;
    }

    public async Task<ErrorOr<PasskeyAssertionStarted>> Handle(
        BeginPasskeyAssertionCommand request,
        CancellationToken cancellationToken)
    {
        // The endpoint answers 404 while passkeys do not run; a switch-off between the two
        // reads is refused as any failed ceremony is.
        var relyingParty = await _feature.RelyingPartyAsync(cancellationToken);
        if (relyingParty is null)
        {
            return PasskeyErrors.VerificationFailed;
        }

        // 1. The live challenge the cookie names, offered again.
        if (!request.ChallengeHandleRepeated && !string.IsNullOrEmpty(request.ChallengeHandle))
        {
            var reusable = await _challengeRepository.GetReusableAssertionAsync(
                _credentialHasher.Candidates(CredentialPurpose.WebAuthnChallenge, request.ChallengeHandle),
                cancellationToken);
            if (reusable is not null)
            {
                return new PasskeyAssertionStarted(reusable.OptionsJson, ChallengeHandle: null, reusable.ExpiresAt);
            }
        }

        // 2. A new challenge, and its handle for a new cookie.
        var optionsJson = _verifier.CreateAssertionOptions(relyingParty);
        var handle = _tokenGenerator.Generate();
        var challenge = WebAuthnChallenge.IssueForAssertion(
            _credentialHasher.Hash(CredentialPurpose.WebAuthnChallenge, handle),
            optionsJson,
            _timeProvider.GetUtcNow().UtcDateTime);
        await _challengeRepository.CreateAsync(challenge, cancellationToken);

        return new PasskeyAssertionStarted(optionsJson, handle, challenge.ExpiresAt);
    }
}
