using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Reauthenticate;

/// <summary>Emails the code a signed-in person proves themself again with.</summary>
/// <param name="UserId">The authenticated user.</param>
/// <param name="CurrentSessionId">The session the access token belongs to.</param>
/// <param name="IpAddress">The caller's address, for the email and the audit column.</param>
public record SendReauthenticationEmailCodeCommand(Guid UserId, Guid? CurrentSessionId, string? IpAddress)
    : IRequest<ErrorOr<EmailCodeSent>>;

/// <summary>Handler for <see cref="SendReauthenticationEmailCodeCommand"/>.</summary>
/// <remarks>
/// Only for an account whose proof takes one (<see cref="ReauthenticationRequirement.SendsEmailCode"/>),
/// on a challenge of its own purpose, so a code sent here answers nothing else. The sender holds
/// every limit: email on, three codes per challenge, the per-window and daily caps, and no code for
/// an emailed second factor that is locked.
/// </remarks>
public sealed class SendReauthenticationEmailCodeCommandHandler
    : IRequestHandler<SendReauthenticationEmailCodeCommand, ErrorOr<EmailCodeSent>>
{
    private readonly IReauthenticationGuard _reauthenticationGuard;
    private readonly IUserRepository _userRepository;
    private readonly ReauthenticationPolicy _policy;
    private readonly AccountEmailCodeChallenge _accountChallenge;
    private readonly IEmailCodeSender _emailCodeSender;
    private readonly ISecondFactorLockout _lockout;

    public SendReauthenticationEmailCodeCommandHandler(
        IReauthenticationGuard reauthenticationGuard,
        IUserRepository userRepository,
        ReauthenticationPolicy policy,
        AccountEmailCodeChallenge accountChallenge,
        IEmailCodeSender emailCodeSender,
        ISecondFactorLockout lockout)
    {
        _reauthenticationGuard = reauthenticationGuard;
        _userRepository = userRepository;
        _policy = policy;
        _accountChallenge = accountChallenge;
        _emailCodeSender = emailCodeSender;
        _lockout = lockout;
    }

    public async Task<ErrorOr<EmailCodeSent>> Handle(
        SendReauthenticationEmailCodeCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _reauthenticationGuard.EnsureLiveSessionAsync(
            request.UserId, request.CurrentSessionId, cancellationToken);
        if (session.IsError)
        {
            return session.Errors;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.ReauthenticationRequired;
        }

        var requirement = await _policy.ForAsync(user, cancellationToken);
        if (!requirement.SendsEmailCode)
        {
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        // As the second step it follows the password, which is what lets the emailed second factor's
        // own checks read it (EmailCodeProofStrategy.MayFollowAtSignIn). As the first proof nothing
        // came before it, and no factor row is asked for (TwoFactorChallenge.RequiresBoundEmailFactor).
        var challenge = await _accountChallenge.GetOrOpenAsync(
            user.Id,
            TwoFactorChallengePurpose.Reauthentication,
            requirement.EmailCode ? AuthenticationMethods.Unknown : AuthenticationMethods.Password,
            request.IpAddress,
            cancellationToken);

        return await _emailCodeSender.SendAsync(user, challenge, request.IpAddress, _lockout.HardLimit, cancellationToken);
    }
}
