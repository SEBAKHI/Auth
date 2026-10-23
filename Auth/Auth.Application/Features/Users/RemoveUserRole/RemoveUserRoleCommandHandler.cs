using Auth.Application.Common;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.RemoveUserRole;

/// <summary>
/// Handler for removing a role from a user.
/// </summary>
public class RemoveUserRoleCommandHandler : IRequestHandler<RemoveUserRoleCommand, ErrorOr<bool>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly IPublisher _publisher;
    private readonly ILogger<RemoveUserRoleCommandHandler> _logger;

    public RemoveUserRoleCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        AccountActionPolicy accountActionPolicy,
        IPublisher publisher,
        ILogger<RemoveUserRoleCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _accountActionPolicy = accountActionPolicy;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(RemoveUserRoleCommand request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Verify role exists
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.RoleId);
        }

        bool removed;
        await using (IAccountAuthorityChange change = await _userRepository.BeginAuthorityChangeAsync(cancellationToken))
        {
            // The account rules: never the system account, only by an administrator
            // who covers the account, and never the last full administrator's *. The
            // last is judged on grants read under the lock this DELETE runs under, so
            // two removals at once cannot both take the last platform *.
            var allowed = await _accountActionPolicy.EnsureAllowedAsync(
                AccountActionRequest.RemoveRole(request.RemovedBy, request.UserId, request.RoleId, request.ApplicationId),
                change,
                cancellationToken);
            if (allowed.IsError)
            {
                return allowed.Errors;
            }

            // The assignment is (user, role, scope), and the DELETE is the one place that
            // names it: no separate "does the user have this role" check. The check this
            // replaced ignored the scope, so an application-scoped assignment passed it,
            // the platform-scoped DELETE then removed nothing, and the removal was still
            // reported, published and audited.
            removed = await change.RemoveRoleAsync(
                request.UserId, request.RoleId, request.ApplicationId, cancellationToken);
            if (removed)
            {
                change.Commit();
            }
        }

        if (!removed)
        {
            return RoleErrors.RoleNotAssigned(request.UserId, request.RoleId);
        }

        _logger.LogInformation(
            "Role removed from user: User {UserId}, Role {RoleId} ({RoleCode}) by {RemovedBy}",
            request.UserId, request.RoleId, role.Code, request.RemovedBy);

        // After the DELETE nothing here waits on the caller: a removal is not
        // retryable (the assignment is gone, so a retry answers 404), and a hang-up
        // must not leave it without its audit row.
        await _publisher.Publish(
            new UserRoleRemovedEvent(
                request.UserId,
                request.RoleId,
                role.Name,
                request.ApplicationId,
                request.RemovedBy),
            CancellationToken.None);

        return true;
    }
}
