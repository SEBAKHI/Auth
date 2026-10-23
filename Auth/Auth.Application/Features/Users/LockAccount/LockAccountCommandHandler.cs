using Auth.Application.Common;
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
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<LockAccountCommandHandler> _logger;

    public LockAccountCommandHandler(
        IUserRepository userRepository,
        AccountActionPolicy accountActionPolicy,
        ICredentialRevocationService credentialRevocation,
        IDomainEventDispatcher eventDispatcher,
        ILogger<LockAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _accountActionPolicy = accountActionPolicy;
        _credentialRevocation = credentialRevocation;
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

        // The account rules, the last full administrator judged under the lock this
        // status write runs under: a locked account cannot sign in.
        var begun = await _accountActionPolicy.BeginAsync(
            new AccountActionRequest(request.LockedBy, user.Id, AccountAction.Lock), cancellationToken);
        if (begun.IsError)
        {
            return begun.Errors;
        }

        await using (IAccountAuthorityChange authorityChange = begun.Value)
        {
            // The lock columns only, decided by the account and written by one
            // statement, never by a whole-row update from this read.
            var change = user.Lock(lockoutEnd, request.LockedBy);
            await authorityChange.SetAccountStatusAsync(user.Id, change, request.LockedBy, cancellationToken);
            authorityChange.Commit();
        }

        // An administrator locking an account is usually answering an incident, so
        // the already-issued access tokens are the point. Ending the session rows
        // leaves them working for their full lifetime, and leaves the refresh
        // token able to mint more. Replaces the direct TerminateAllForUserAsync —
        // appending to it would find no active sessions left to blacklist.
        await _credentialRevocation.RevokeAllCredentialsAsync(
            request.UserId,
            request.LockedBy,
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
