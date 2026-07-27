using Auth.Application.Features.AccountDeletion.Common;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.ConfirmPublicDeletion;

/// <summary>
/// Handler for confirming a public deletion request: email possession (OTP)
/// is the re-authentication, then the shared deletion-request pipeline runs.
/// Idempotent: confirming an already-pending deletion succeeds generically.
/// </summary>
public class ConfirmPublicDeletionCommandHandler
    : IRequestHandler<ConfirmPublicDeletionCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly DeletionOtpService _otpService;
    private readonly AccountDeletionRequestor _requestor;

    public ConfirmPublicDeletionCommandHandler(
        IUserRepository userRepository,
        IAccountDeletionRequestRepository requestRepository,
        DeletionOtpService otpService,
        AccountDeletionRequestor requestor)
    {
        _userRepository = userRepository;
        _requestRepository = requestRepository;
        _otpService = otpService;
        _requestor = requestor;
    }

    public async Task<ErrorOr<Success>> Handle(
        ConfirmPublicDeletionCommand request, CancellationToken cancellationToken)
    {
        // Unknown email, wrong code, expired code and exhausted attempts are
        // all the same generic error.
        var otpResult = await _otpService.VerifyAsync(request.Email, request.OtpCode, cancellationToken);
        if (otpResult.IsError)
        {
            return otpResult.Errors;
        }

        var userId = otpResult.Value.UserId;
        var user = userId.HasValue
            ? await _userRepository.GetByIdIncludeDeletedAsync(userId.Value, cancellationToken)
            : null;
        if (user is null)
        {
            return AccountDeletionErrors.InvalidOtp;
        }

        // Idempotent: a deletion is already underway for this account, so
        // confirming again is a success, not a conflict.
        if (user.IsDeleted)
        {
            var active = await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken);
            return active is not null
                ? Result.Success
                : AccountDeletionErrors.InvalidOtp;
        }

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.PublicWeb, cancellationToken);
        if (result.IsError)
        {
            // The insert race is the same idempotent case; real conflicts
            // (owned organizations, system account) surface to the caller.
            return result.FirstError == UserErrors.DeletionAlreadyRequested
                ? Result.Success
                : result.Errors;
        }

        return Result.Success;
    }
}
