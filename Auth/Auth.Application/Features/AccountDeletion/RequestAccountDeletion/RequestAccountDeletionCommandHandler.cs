using Auth.Application.Features.AccountDeletion.Common;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RequestAccountDeletion;

/// <summary>
/// Handler for the authenticated in-app deletion request: fresh
/// re-authentication first, then the shared deletion-request pipeline.
/// </summary>
public class RequestAccountDeletionCommandHandler
    : IRequestHandler<RequestAccountDeletionCommand, ErrorOr<AccountDeletionRequestedResult>>
{
    private readonly IUserRepository _userRepository;
    private readonly DeletionOtpService _otpService;
    private readonly AccountDeletionRequestor _requestor;

    public RequestAccountDeletionCommandHandler(
        IUserRepository userRepository,
        DeletionOtpService otpService,
        AccountDeletionRequestor requestor)
    {
        _userRepository = userRepository;
        _otpService = otpService;
        _requestor = requestor;
    }

    public async Task<ErrorOr<AccountDeletionRequestedResult>> Handle(
        RequestAccountDeletionCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Refuse the foreseeable conflicts before the code is spent. Verifying
        // consumes it, so discovering "you still own an organization with
        // members" afterwards would leave the user holding a dead code and one
        // fewer issuance in the rate-limit window. Safe to disclose here: the
        // caller is already authenticated as this account.
        var requestable = await _requestor.EnsureRequestableAsync(user, cancellationToken);
        if (requestable.IsError)
        {
            return requestable.Errors;
        }

        // Fresh re-authentication: a stolen session alone must never be able to
        // schedule an account's destruction. The factor is possession of the
        // account's mailbox — one code path for every account, the same one the
        // public wizard uses. The password is deliberately not accepted: it
        // does not exist for external-only accounts, and mailbox possession
        // already outranks it (it is what password reset itself trusts).
        var otpResult = await _otpService.VerifyForUserAsync(user, request.OtpCode, cancellationToken);
        if (otpResult.IsError)
        {
            return otpResult.Errors;
        }

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.InApp, cancellationToken);
        if (result.IsError)
        {
            return result.Errors;
        }

        return new AccountDeletionRequestedResult(result.Value.GraceEndsAtUtc);
    }
}
