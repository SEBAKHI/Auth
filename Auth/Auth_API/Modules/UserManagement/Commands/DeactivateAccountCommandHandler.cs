using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.UserManagement.Commands;

/// <summary>
/// Handler for the deactivate account command.
/// </summary>
public class DeactivateAccountCommandHandler : IRequestHandler<DeactivateAccountCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly ILogger<DeactivateAccountCommandHandler> _logger;

    public DeactivateAccountCommandHandler(
        IUserRepository userRepository,
        IUserSessionRepository sessionRepository,
        ILogger<DeactivateAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        DeactivateAccountCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        user.Deactivate(request.DeactivatedBy);
        await _userRepository.UpdateAsync(user, cancellationToken);

        // Terminate all active sessions for the deactivated user
        await _sessionRepository.TerminateAllForUserAsync(
            request.UserId,
            "Account deactivated",
            cancellationToken);

        _logger.LogInformation(
            "User {UserId} account deactivated by {DeactivatedBy}",
            request.UserId,
            request.DeactivatedBy);

        return Result.Success;
    }
}
