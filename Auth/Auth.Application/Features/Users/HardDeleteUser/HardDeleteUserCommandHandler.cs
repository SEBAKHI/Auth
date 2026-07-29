using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.HardDeleteUser;

/// <summary>
/// Handler for permanently destroying a soft-deleted user via the staged
/// destruction routine (tombstone, crypto-shred, log anonymization, cascade).
/// </summary>
public class HardDeleteUserCommandHandler : IRequestHandler<HardDeleteUserCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly OwnedOrganizationDeletionGuard _organizationGuard;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly ILogger<HardDeleteUserCommandHandler> _logger;

    public HardDeleteUserCommandHandler(
        IUserRepository userRepository,
        OwnedOrganizationDeletionGuard organizationGuard,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        ILogger<HardDeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _organizationGuard = organizationGuard;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(HardDeleteUserCommand request, CancellationToken cancellationToken)
    {
        // The system account is the reattribution target for purged actor
        // references and the author of the seed data — it must never be
        // removable, whatever its flags say.
        if (request.Id == WellKnownUserIds.System)
        {
            return UserErrors.CannotDeleteSystemUser;
        }

        var user = await _userRepository.GetByIdIncludeDeletedAsync(request.Id, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.Id);
        }

        if (user.IsSystemUser)
        {
            return UserErrors.CannotDeleteSystemUser;
        }

        // Hard deletion is a second, separate step: only accounts that already
        // went through the soft delete (hidden, blocked from login) qualify.
        if (!user.IsDeleted)
        {
            return UserErrors.NotSoftDeleted;
        }

        // The soft delete already removed sole-owned organizations, but the
        // ownership FK has no cascade — if an owned organization still exists
        // (legacy data), the shared guard applies the same rules: block when
        // other members depend on it, remove it when the owner was its only member.
        var organizationsResult = await _organizationGuard.EnsureDeletableAsync(request.Id, cancellationToken);
        if (organizationsResult.IsError)
        {
            return organizationsResult.Errors;
        }

        // Belt-and-braces for accounts soft-deleted before revocation-on-delete
        // existed: blacklist any still-outstanding access tokens before the
        // purge removes the session rows they would be checked against.
        await _credentialRevocation.RevokeAllCredentialsAsync(
            request.Id, request.DeletedBy, "Account permanently deleted", cancellationToken);

        var purged = await _userRepository.HardDeleteAsync(request.Id, cancellationToken);
        if (!purged)
        {
            // Lost a race with a concurrent write on the account row.
            return UserErrors.NotSoftDeleted;
        }

        _logger.LogInformation(
            "User permanently deleted: {UserId} by {DeletedBy}",
            request.Id, request.DeletedBy);

        await _publisher.Publish(
            new UserHardDeletedEvent(request.Id, user.Email, request.DeletedBy),
            cancellationToken);

        return Result.Success;
    }
}
