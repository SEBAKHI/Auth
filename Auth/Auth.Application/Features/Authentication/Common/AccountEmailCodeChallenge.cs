using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The challenge a signed-in account's emailed code is sent on (S24): binding the
/// emailed code (<see cref="TwoFactorChallengePurpose.EmailEnrollment"/>) or letting it
/// go (<see cref="TwoFactorChallengePurpose.EmailDisable"/>).
/// </summary>
public sealed class AccountEmailCodeChallenge
{
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AccountEmailCodeChallenge(
        ITwoFactorChallengeRepository challengeRepository,
        ICredentialHasher credentialHasher,
        IJwtTokenService jwtTokenService)
    {
        _challengeRepository = challengeRepository;
        _credentialHasher = credentialHasher;
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// The account's live challenge of <paramref name="purpose"/>, or a new one when
    /// none is live. Answered by the signed-in account, never by a token: the token
    /// exists only because the column needs a unique fingerprint, and nobody receives it.
    /// </summary>
    public async Task<TwoFactorChallenge> GetOrOpenAsync(
        Guid userId,
        TwoFactorChallengePurpose purpose,
        AuthenticationMethods primaryMethod,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var challenge = await _challengeRepository.GetLiveForUserAsync(userId, purpose, cancellationToken);
        if (challenge is not null)
        {
            return challenge;
        }

        challenge = TwoFactorChallenge.Create(
            userId,
            _credentialHasher.Hash(CredentialPurpose.TwoFactorChallenge, _jwtTokenService.GenerateRefreshToken()),
            ipAddress,
            primaryMethod: primaryMethod,
            purpose: purpose);

        await _challengeRepository.CreateAsync(challenge, cancellationToken);
        return challenge;
    }
}
