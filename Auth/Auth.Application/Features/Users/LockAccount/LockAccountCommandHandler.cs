using Auth.Application.Common;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
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
    private readonly RecentPasskeyRevocation _recentPasskeys;
    private readonly ITwoFactorBindCodeRepository _bindCodes;
    private readonly ILogger<LockAccountCommandHandler> _logger;

    public LockAccountCommandHandler(
        IUserRepository userRepository,
        AccountActionPolicy accountActionPolicy,
        ICredentialRevocationService credentialRevocation,
        IDomainEventDispatcher eventDispatcher,
        RecentPasskeyRevocation recentPasskeys,
        ITwoFactorBindCodeRepository bindCodes,
        ILogger<LockAccountCommandHandler> logger)
    {
        _recentPasskeys = recentPasskeys;
        _bindCodes = bindCodes;
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

        // S20 (D-T19 D): the passkeys registered in the 72 hours before the lock go once it has
        // committed. Cut once, now.
        var passkeyCutoff = _recentPasskeys.CutoffNow();

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

        // From the commit on, what takes the account back runs to its end whatever the
        // administrator's browser does (REVIEW 150 F1, F2; REVIEW-FIXES 150 F14): no step
        // takes the request's token. The sessions first, so a passkey registration in flight
        // from one of them either committed already, and the revocation below catches it, or
        // finds its session ended and stores nothing; then the passkeys, even when ending the
        // sessions throws, whose exception surfaces after it.
        var revocationReason = $"Account locked: {request.Reason}";
        var recentRevoked = 0;
        try
        {
            // An administrator locking an account is usually answering an incident, so
            // the already-issued access tokens are the point. Ending the session rows
            // leaves them working for their full lifetime, and leaves the refresh
            // token able to mint more. Replaces the direct TerminateAllForUserAsync —
            // appending to it would find no active sessions left to blacklist.
            await _credentialRevocation.RevokeAllCredentialsAsync(
                request.UserId,
                request.LockedBy,
                revocationReason,
                CancellationToken.None);
        }
        finally
        {
            // S20 (D-T19 D): a lock answers an incident, and a passkey registered in the 72
            // hours before it may be the intruder's door; it goes, the older ones stay, and
            // the owner is told which one and why (REVIEW 150 F12).
            recentRevoked = await _recentPasskeys.RevokeRecentAsync(
                user, passkeyCutoff, PasskeyRemovalReasons.AdminLock, request.LockedBy);
        }

        // Write, then read, on both sides (REVIEW 151 F2): a passkey sign-in in flight with one
        // of those passkeys writes its session, then reads the passkey again. One whose session
        // was written after the sweep above is ended by this second one; a later one finds the
        // passkey revoked and ends its own.
        if (recentRevoked > 0)
        {
            await _credentialRevocation.RevokeAllCredentialsAsync(
                request.UserId,
                request.LockedBy,
                revocationReason,
                CancellationToken.None);
        }

        // REVIEW-FIXES 150 F18 (g): a lock leaves the password as it was, so an emailed code to
        // bind a factor, asked for before it, must not bind one once the lock is lifted. Never
        // failing the lock, which is set.
        try
        {
            await _bindCodes.InvalidateOutstandingForUserAsync(request.UserId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "User {UserId} was locked, but the outstanding codes to bind a second factor could not be ended",
                request.UserId);
        }

        _logger.LogInformation(
            "User {UserId} account locked by {LockedBy}. Reason: {Reason}. Lock ends: {LockoutEnd}",
            request.UserId,
            request.LockedBy,
            request.Reason,
            lockoutEnd?.ToString() ?? "Indefinite");

        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return Result.Success;
    }
}
