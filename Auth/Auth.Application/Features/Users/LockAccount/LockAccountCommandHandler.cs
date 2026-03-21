using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.LockAccount;

/// <summary>
/// Handler for the lock account command.
/// </summary>
public class LockAccountCommandHandler : IRequestHandler<LockAccountCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<LockAccountCommandHandler> _logger;

    public LockAccountCommandHandler(
        IUserRepository userRepository,
        IUserSessionRepository sessionRepository,
        IDomainEventDispatcher eventDispatcher,
        ILogger<LockAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _sessionRepository = sessionRepository;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        LockAccountCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Calculate lock end time
        DateTime? lockoutEnd = request.LockDurationMinutes.HasValue
            ? DateTime.UtcNow.AddMinutes(request.LockDurationMinutes.Value)
            : null; // Indefinite lock

        user.Lock(lockoutEnd, request.LockedBy);
        await _userRepository.UpdateAsync(user, cancellationToken);

        // Terminate all active sessions for the locked user
        await _sessionRepository.TerminateAllForUserAsync(
            request.UserId,
            $"Account locked: {request.Reason}",
            cancellationToken);

        _logger.LogInformation(
            "User {UserId} account locked by {LockedBy}. Reason: {Reason}. Lock ends: {LockoutEnd}",
            request.UserId,
            request.LockedBy,
            request.Reason,
            lockoutEnd?.ToString() ?? "Indefinite");

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return Result.Success;
    }
}
