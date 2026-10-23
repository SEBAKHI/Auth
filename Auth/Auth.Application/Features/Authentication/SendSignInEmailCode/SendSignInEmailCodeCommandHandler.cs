using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SendSignInEmailCode;

/// <summary>
/// Handler that emails the code completing a pending sign-in, for an account whose
/// second factor is the emailed code (S24).
/// </summary>
/// <remarks>
/// <para>
/// Only on request (D-T30 B): a sign-in never emails a code on its own, so whoever
/// holds a leaked password floods no mailbox without asking, and each ask passes the
/// limits on sending — three per sign-in, the account's per-window and daily caps —
/// in one statement.
/// </para>
/// <para>
/// Only for a sign-in the password opened (D-T35 A): a provider sign-in or a
/// verify-email link already went through the mailbox, so a code sent there would
/// prove nothing the first step did not. Such a sign-in finishes with a recovery code.
/// </para>
/// <para>
/// The token is the only credential, and every dead challenge — unknown, expired,
/// used — is one answer, as at verify.
/// </para>
/// </remarks>
public class SendSignInEmailCodeCommandHandler
    : IRequestHandler<SendSignInEmailCodeCommand, ErrorOr<SignInEmailCodeResponse>>
{
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly IUserRepository _userRepository;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IEmailCodeSender _emailCodeSender;
    private readonly ISecondFactorLockout _lockout;

    public SendSignInEmailCodeCommandHandler(
        ITwoFactorChallengeRepository challengeRepository,
        ITwoFactorStateStore twoFactorStateStore,
        IUserRepository userRepository,
        ICredentialHasher credentialHasher,
        IEmailCodeSender emailCodeSender,
        ISecondFactorLockout lockout)
    {
        _challengeRepository = challengeRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _userRepository = userRepository;
        _credentialHasher = credentialHasher;
        _emailCodeSender = emailCodeSender;
        _lockout = lockout;
    }

    public async Task<ErrorOr<SignInEmailCodeResponse>> Handle(
        SendSignInEmailCodeCommand request,
        CancellationToken cancellationToken)
    {
        // A sign-in challenge only: the lookup reads no row of another purpose.
        var candidates = _credentialHasher.Candidates(CredentialPurpose.TwoFactorChallenge, request.ChallengeToken);
        var challenge = await _challengeRepository.GetByTokenHashAsync(candidates, cancellationToken);
        if (challenge is null || !challenge.IsValid)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        var user = await _userRepository.GetByIdAsync(challenge.UserId, cancellationToken);
        if (user is null)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        // The account's state may have changed since the password: what verify
        // would refuse, a code for it is not sent.
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            return statusCheck.Errors;
        }

        if (user.LockKind(DateTime.UtcNow) == AccountLockKind.Admin)
        {
            return user.LockRefusal(AccountLockKind.Admin);
        }

        if (!EmailCodeProofStrategy.MayFollow(challenge.PrimaryMethod))
        {
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        // An account whose factor is the authenticator app asks nothing of its
        // mailbox here: the answer names no factor beyond what the sign-in offered.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(user.Id, cancellationToken);
        if (snapshot?.HasEmailCode != true)
        {
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        var sent = await _emailCodeSender.SendAsync(
            user, challenge, request.IpAddress, _lockout.HardLimit, cancellationToken);
        if (sent.IsError)
        {
            return sent.Errors;
        }

        return new SignInEmailCodeResponse(sent.Value.ExpiresAt, sent.Value.MaskedEmail, sent.Value.SendsRemaining);
    }
}
