using Auth.Application.Common;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.PublicRequestDeletion;

/// <summary>
/// Handler for the public no-login deletion request. Anti-enumeration is
/// absolute here: unknown email, rate-limited email and failed sends all
/// return the identical generic acknowledgment.
/// </summary>
public class PublicRequestDeletionCommandHandler
    : IRequestHandler<PublicRequestDeletionCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly DeletionOtpService _otpService;
    private readonly ILogger<PublicRequestDeletionCommandHandler> _logger;

    public PublicRequestDeletionCommandHandler(
        IUserRepository userRepository,
        DeletionOtpService otpService,
        ILogger<PublicRequestDeletionCommandHandler> logger)
    {
        _userRepository = userRepository;
        _otpService = otpService;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        PublicRequestDeletionCommand request, CancellationToken cancellationToken)
    {
        // Accounts already pending deletion are invisible to this lookup —
        // which is exactly right: no second OTP, same generic acknowledgment.
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            _logger.LogInformation(
                "Public deletion request for unknown email {Email}; acknowledged generically",
                EmailMasking.Mask(request.Email));
            return Result.Success;
        }

        var issueResult = await _otpService.IssueAsync(user, cancellationToken);
        if (issueResult.IsError)
        {
            // Rate limit or send failure — logged inside the service; the
            // response must stay indistinguishable from the success path.
            return Result.Success;
        }

        return Result.Success;
    }
}
