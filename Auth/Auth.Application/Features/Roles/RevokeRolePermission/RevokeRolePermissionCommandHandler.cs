using Auth.Application.Common;
using Auth.Domain.Events;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.RevokeRolePermission;

/// <summary>
/// Handler for removing a permission from a role.
/// </summary>
/// <remarks>
/// <para>
/// Only a holder removes (owner decision D-226-1 (a)): the remover must hold the code
/// removed, by the matching the grant side uses, on every role and for every code — a
/// role can lose only what its editor could have put in it. Removing <c>*</c> from a
/// platform role also follows the last-full-administrator rule, decided on grants
/// read under the lock the DELETE runs under (<see cref="AccountActionPolicy"/>).
/// </para>
/// <para>
/// This rule once read the other way: removal was left free of the no-amplification
/// rule so that a mis-granted permission would not become removable only by a holder
/// of it. That no longer needs saying: a holder of <c>*</c> covers every code, and the
/// last-full-administrator rule keeps one, so every granted permission stays removable
/// by someone. Removal is gated on <c>roles:update</c> as well.
/// </para>
/// </remarks>
public class RevokeRolePermissionCommandHandler
    : IRequestHandler<RevokeRolePermissionCommand, ErrorOr<Success>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly IPublisher _publisher;
    private readonly ILogger<RevokeRolePermissionCommandHandler> _logger;

    public RevokeRolePermissionCommandHandler(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        AccountActionPolicy accountActionPolicy,
        IPublisher publisher,
        ILogger<RevokeRolePermissionCommandHandler> logger)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _accountActionPolicy = accountActionPolicy;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        RevokeRolePermissionCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound(request.RoleId);
        }

        var granted = await _permissionRepository.GetRolePermissionsAsync(
            request.RoleId, cancellationToken);
        var permission = granted.FirstOrDefault(p => p.Id == request.PermissionId);
        if (permission is null)
        {
            return PermissionErrors.PermissionNotGranted;
        }

        // Only a holder of the code removes it, and never the last full
        // administrator's *: the coverage is decided before the lock, the count under
        // the lock this DELETE runs under, so two removals at once cannot both take
        // the last platform *.
        var begun = await _accountActionPolicy.BeginRolePermissionRemovalAsync(
            new RolePermissionRemovalRequest(request.RevokedBy, role.Id, role.ApplicationId, permission.Code.Value),
            cancellationToken);
        if (begun.IsError)
        {
            return begun.Errors;
        }

        bool removed;
        await using (IAccountAuthorityChange change = begun.Value)
        {
            removed = await change.RevokeFromRoleAsync(role.Id, permission.Id, cancellationToken);
            if (removed)
            {
                change.Commit();
            }
        }

        if (!removed)
        {
            // Removed by someone else since the read above: nothing happened here, so
            // nothing is reported or audited.
            return PermissionErrors.PermissionNotGranted;
        }

        _logger.LogInformation(
            "Permission {PermissionCode} removed from role {RoleId} ({RoleName}) by {RevokedBy}",
            permission.Code.Value, role.Id, role.Name, request.RevokedBy);

        // After the DELETE nothing here waits on the caller: the permission is gone, so
        // a hang-up must not leave the removal without its audit row.
        await _publisher.Publish(
            new RolePermissionRevokedEvent(
                request.RoleId,
                role.Name,
                request.PermissionId,
                permission.Code.Value,
                request.RevokedBy),
            CancellationToken.None);

        return Result.Success;
    }
}
