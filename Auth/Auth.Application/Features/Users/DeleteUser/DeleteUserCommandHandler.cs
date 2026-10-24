using Auth.Application.Common;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
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
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly OwnedOrganizationDeletionGuard _organizationGuard;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IUserRepository userRepository,
        AccountActionPolicy accountActionPolicy,
        OwnedOrganizationDeletionGuard organizationGuard,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _accountActionPolicy = accountActionPolicy;
        _organizationGuard = organizationGuard;
        _credentialRevocation = credentialRevocation;
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

        // The account rules, before anything is removed: never the system account
        // (User.CannotDeleteSystemUser), only by an administrator who covers the
        // account, and never the last full administrator — judged under the lock this
        // soft delete runs under.
        var begun = await _accountActionPolicy.BeginAsync(
            new AccountActionRequest(request.DeletedBy, user.Id, AccountAction.Delete), cancellationToken);
        if (begun.IsError)
        {
            return begun.Errors;
        }

        await using (IAccountAuthorityChange change = begun.Value)
        {
            // Shared owned-organization rule: block while an owned organization
            // still has other members, delete sole-member owned ones with the account.
            var organizationsResult = await _organizationGuard.EnsureDeletableAsync(request.Id, cancellationToken);
            if (organizationsResult.IsError)
            {
                return organizationsResult.Errors;
            }

            await change.DeleteAsync(request.Id, cancellationToken);
            change.Commit();
        }

        // A deleted account must be logged out everywhere immediately: the
        // IsDeleted flag only blocks NEW logins, so without this wipe the
        // account's existing sessions, refresh tokens and SSO cookies would
        // keep working until they expire.
        await _credentialRevocation.RevokeAllCredentialsAsync(
            request.Id, request.DeletedBy, "Account deleted", cancellationToken);

        _logger.LogInformation(
            "User deleted: {UserId} by {DeletedBy}",
            request.Id, request.DeletedBy);

        await _publisher.Publish(
            new UserDeletedEvent(request.Id, user.Email, request.DeletedBy),
            cancellationToken);

        return Result.Success;
    }
}
