using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.ActivateAccount;

/// <summary>
/// Handler for the activate account command.
/// </summary>
public class ActivateAccountCommandHandler : IRequestHandler<ActivateAccountCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<ActivateAccountCommandHandler> _logger;

    public ActivateAccountCommandHandler(
        IUserRepository userRepository,
        ILogger<ActivateAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ActivateAccountCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        user.Activate(request.ActivatedBy);
        await _userRepository.UpdateAsync(user, cancellationToken);

        _logger.LogInformation(
            "User {UserId} account activated by {ActivatedBy}",
            request.UserId,
            request.ActivatedBy);

        return Result.Success;
    }
}
