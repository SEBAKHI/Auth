using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Interfaces;
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
    private readonly IPasswordHasher _passwordHasher;
    private readonly DeletionOtpService _otpService;
    private readonly AccountDeletionRequestor _requestor;

    public RequestAccountDeletionCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        DeletionOtpService otpService,
        AccountDeletionRequestor requestor)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
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

        // Fresh re-authentication: a stolen session alone must never be able
        // to schedule an account's destruction.
        if (user.PasswordHash is not null)
        {
            if (string.IsNullOrEmpty(request.Password)
                || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                return UserErrors.InvalidCurrentPassword;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(request.OtpCode))
            {
                return AccountDeletionErrors.InvalidOtp;
            }

            var otpResult = await _otpService.VerifyAsync(user.Email, request.OtpCode, cancellationToken);
            if (otpResult.IsError)
            {
                return otpResult.Errors;
            }
        }

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.InApp, cancellationToken);
        if (result.IsError)
        {
            return result.Errors;
        }

        return new AccountDeletionRequestedResult(result.Value.GraceEndsAtUtc);
    }
}
