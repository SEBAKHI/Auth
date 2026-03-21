using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.DeleteUser;

/// <summary>
/// Handler for deleting a user.
/// </summary>
public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IUserRepository userRepository,
        IPublisher publisher,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.Id);
        }

        // Cannot delete system users
        if (user.IsSystemUser)
        {
            return Error.Forbidden(
                code: "User.CannotDeleteSystemUser",
                description: "System users cannot be deleted.");
        }

        await _userRepository.DeleteAsync(request.Id, cancellationToken);

        _logger.LogInformation(
            "User deleted: {UserId} by {DeletedBy}",
            request.Id, request.DeletedBy);

        await _publisher.Publish(
            new UserDeletedEvent(request.Id, user.Email, request.DeletedBy),
            cancellationToken);

        return Result.Success;
    }
}
