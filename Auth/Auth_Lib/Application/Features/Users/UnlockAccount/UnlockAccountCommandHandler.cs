using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Users.UnlockAccount;

/// <summary>
/// Handler for the unlock account command.
/// </summary>
public class UnlockAccountCommandHandler : IRequestHandler<UnlockAccountCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UnlockAccountCommandHandler> _logger;

    public UnlockAccountCommandHandler(
        IUserRepository userRepository,
        ILogger<UnlockAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        UnlockAccountCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        user.Unlock(request.UnlockedBy);
        await _userRepository.UpdateAsync(user, cancellationToken);

        _logger.LogInformation(
            "User {UserId} account unlocked by {UnlockedBy}",
            request.UserId,
            request.UnlockedBy);

        return Result.Success;
    }
}
