using Auth.Application.Features.AccountDeletion.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.SendDeletionReauthCode;

/// <summary>
/// Handler for issuing a deletion re-authentication code to the
/// authenticated user.
/// </summary>
public class SendDeletionReauthCodeCommandHandler
    : IRequestHandler<SendDeletionReauthCodeCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly DeletionOtpService _otpService;

    public SendDeletionReauthCodeCommandHandler(
        IUserRepository userRepository,
        DeletionOtpService otpService)
    {
        _userRepository = userRepository;
        _otpService = otpService;
    }

    public async Task<ErrorOr<Success>> Handle(
        SendDeletionReauthCodeCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        return await _otpService.IssueAsync(user, cancellationToken);
    }
}
