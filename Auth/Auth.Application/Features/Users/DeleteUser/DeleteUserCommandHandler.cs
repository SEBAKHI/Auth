using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
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
    private readonly OwnedOrganizationDeletionGuard _organizationGuard;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IUserRepository userRepository,
        OwnedOrganizationDeletionGuard organizationGuard,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
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

        // Cannot delete system users. The well-known id check is the effective
        // guard: no query populates IsSystemUser (the Users table has no such
        // column), so the flag alone would wave the system account through.
        if (user.IsSystemUser || request.Id == WellKnownUserIds.System)
        {
            return UserErrors.CannotDeleteSystemUser;
        }

        // Shared owned-organization rule: block while an owned organization
        // still has other members, delete sole-member owned ones with the account.
        var organizationsResult = await _organizationGuard.EnsureDeletableAsync(request.Id, cancellationToken);
        if (organizationsResult.IsError)
        {
            return organizationsResult.Errors;
        }

        await _userRepository.DeleteAsync(request.Id, cancellationToken);

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
