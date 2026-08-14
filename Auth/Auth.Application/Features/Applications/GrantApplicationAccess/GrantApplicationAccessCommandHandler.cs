using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GrantApplicationAccess;

/// <summary>
/// Handler for inviting a user to an application.
/// </summary>
public class GrantApplicationAccessCommandHandler : IRequestHandler<GrantApplicationAccessCommand, ErrorOr<Success>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _accessRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<GrantApplicationAccessCommandHandler> _logger;

    public GrantApplicationAccessCommandHandler(
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository accessRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPublisher publisher,
        ILogger<GrantApplicationAccessCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _accessRepository = accessRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        GrantApplicationAccessCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        Role? role = null;
        if (request.RoleId.HasValue)
        {
            role = await _roleRepository.GetByIdAsync(request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return RoleErrors.NotFound(request.RoleId.Value);
            }

            // A role owned by another application cannot be granted here.
            // Platform-wide roles (null owner) may be scoped to any application.
            if (role.ApplicationId.HasValue && role.ApplicationId.Value != request.ApplicationId)
            {
                return RoleErrors.NotFound(request.RoleId.Value);
            }
        }

        // The unique (application, user) constraint means a previously revoked
        // or lapsed invitation must be reinstated on its own row, not inserted
        // beside itself — which also keeps the earlier trial in the audit trail.
        var existing = await _accessRepository.GetGrantAsync(
            request.ApplicationId, request.UserId, cancellationToken);

        if (existing is not null && existing.IsValid())
        {
            return ApplicationErrors.UserAccessAlreadyGranted(request.UserId);
        }

        if (existing is not null)
        {
            existing.Reinstate(request.GrantedBy, request.ExpiresAt, request.Note);
            await _accessRepository.UpdateGrantAsync(existing, cancellationToken);
        }
        else
        {
            var grant = ApplicationUserAccess.Create(
                request.ApplicationId,
                request.UserId,
                request.GrantedBy,
                request.ExpiresAt,
                request.Note);

            await _accessRepository.CreateGrantAsync(grant, cancellationToken);
        }

        if (role is not null)
        {
            // Scoped to this application, so the role travels only in tokens
            // minted for it. A duplicate is not an error here: the invitation is
            // the point of this command, and re-inviting someone who kept their
            // role should not fail.
            var existingRole = await _userRepository.GetUserRoleAsync(
                request.UserId, role.Id, request.ApplicationId, cancellationToken);

            if (existingRole is null || !existingRole.IsValid())
            {
                var userRole = UserRole.Create(
                    userId: request.UserId,
                    roleId: role.Id,
                    assignedBy: request.GrantedBy,
                    applicationId: request.ApplicationId,
                    expiresAt: request.ExpiresAt);

                await _roleRepository.AssignToUserAsync(userRole, cancellationToken);

                await _publisher.Publish(
                    new RoleAssignedEvent(request.UserId, role.Id, role.Name, request.GrantedBy),
                    cancellationToken);
            }
        }

        _logger.LogInformation(
            "User {UserId} granted access to application {ApplicationId} ({ApplicationCode}) by {GrantedBy}",
            request.UserId, application.Id, application.Code, request.GrantedBy);

        await _publisher.Publish(
            new ApplicationAccessGrantedEvent(
                application.Id, application.Code, request.UserId, request.ExpiresAt, request.GrantedBy),
            cancellationToken);

        return Result.Success;
    }
}
