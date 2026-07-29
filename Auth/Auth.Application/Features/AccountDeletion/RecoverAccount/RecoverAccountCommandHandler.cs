using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RecoverAccount;

/// <summary>
/// Handler for password-based grace-period recovery. Anti-enumeration: a live
/// account, an unknown account, an admin-soft-deleted account and a wrong
/// password all return the identical invalid-credentials error — pending
/// deletion is only ever revealed to a caller holding valid credentials.
/// </summary>
public class RecoverAccountCommandHandler
    : IRequestHandler<RecoverAccountCommand, ErrorOr<LoginResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AccountDeletionRecoverer _recoverer;
    private readonly ILogger<RecoverAccountCommandHandler> _logger;

    public RecoverAccountCommandHandler(
        IUserRepository userRepository,
        IAccountDeletionRequestRepository requestRepository,
        IPasswordHasher passwordHasher,
        AccountDeletionRecoverer recoverer,
        ILogger<RecoverAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _requestRepository = requestRepository;
        _passwordHasher = passwordHasher;
        _recoverer = recoverer;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        RecoverAccountCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailIncludeDeletedAsync(request.Email, cancellationToken);
        if (user is null || !user.IsDeleted || user.PasswordHash is null)
        {
            return UserErrors.InvalidCredentials;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning(
                "Failed recovery attempt for pending-deletion account {UserId} from {IpAddress}",
                user.Id, request.IpAddress);
            return UserErrors.InvalidCredentials;
        }

        // Lockout keeps its usual precedence: valid credentials on a locked
        // account do not open the recovery path early.
        if (user.IsLockedOut())
        {
            return UserErrors.AccountLockedUntil(user.LockoutEnd);
        }

        // Admin-soft-deleted accounts have no request row: recovery stays an
        // admin decision, and the caller learns nothing.
        var deletionRequest = await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken);
        if (deletionRequest is null || deletionRequest.Status != AccountDeletionStatus.PendingGrace)
        {
            return UserErrors.InvalidCredentials;
        }

        return await _recoverer.RecoverAsync(
            user, deletionRequest, request.TwoFactorCode, request.IpAddress, request.UserAgent, cancellationToken);
    }
}
