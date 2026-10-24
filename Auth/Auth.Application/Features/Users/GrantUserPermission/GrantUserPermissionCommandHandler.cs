using Auth.Domain.Events;
using Auth.Application.Common;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Features.Users.GrantUserPermission;

/// <summary>
/// Handler for granting a direct permission to a user.
/// </summary>
public class GrantUserPermissionCommandHandler : IRequestHandler<GrantUserPermissionCommand, ErrorOr<bool>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly PlatformGrantFactorGuard _factorGuard;
    private readonly ApplicationAccessWriter _accessWriter;
    private readonly IPublisher _publisher;
    private readonly ILogger<GrantUserPermissionCommandHandler> _logger;

    public GrantUserPermissionCommandHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        IApplicationRepository applicationRepository,
        PermissionGrantGuard grantGuard,
        PlatformGrantFactorGuard factorGuard,
        ApplicationAccessWriter accessWriter,
        IPublisher publisher,
        ILogger<GrantUserPermissionCommandHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _applicationRepository = applicationRepository;
        _grantGuard = grantGuard;
        _factorGuard = factorGuard;
        _accessWriter = accessWriter;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(GrantUserPermissionCommand request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Verify permission exists
        var permission = await _permissionRepository.GetByIdAsync(request.PermissionId, cancellationToken);
        if (permission == null)
        {
            return PermissionErrors.NotFound(request.PermissionId);
        }

        // The scope the grant lands in. A permission owned by an application works
        // only in that application's tokens, so it is granted there whatever the
        // request names, and never inside another application, as for a role. A
        // platform permission keeps the scope the request names: none, or an
        // application that must exist.
        Guid? scope;
        if (permission.ApplicationId is { } owner)
        {
            if (request.ApplicationId is { } requested && requested != owner)
            {
                return PermissionErrors.NotFound(request.PermissionId);
            }

            scope = owner;
        }
        else
        {
            scope = request.ApplicationId;
        }

        ApplicationEntity? application = null;
        if (scope is { } applicationId)
        {
            application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            if (application is null)
            {
                return ApplicationErrors.NotFound(applicationId);
            }
        }

        // No amplification: the endpoint gate asks whether this actor may grant
        // at all, which on its own would let a users:manage-permissions holder
        // hand itself the global "*" row and become super-administrator in one
        // call. This asks the second question — whether it holds what it is
        // handing over. Authority inside an application is what the application
        // page gates with applications:update, so a grant at an application's
        // scope asks for it too, in the same read.
        IEnumerable<string> requestedCodes = application is null
            ? [permission.Code.Value]
            : [permission.Code.Value, PermissionCodes.Applications.Update];
        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            request.GrantedBy, requestedCodes, cancellationToken);
        if (canGrant.IsError)
        {
            _logger.LogWarning(
                "Blocked grant of {PermissionCode} to user {UserId} at application {ApplicationId}: actor {GrantedBy} does not hold it",
                permission.Code.Value, request.UserId, scope, request.GrantedBy);
            return canGrant.Errors;
        }

        // A grant is (user, permission, scope): a platform grant must not block
        // granting the same permission inside an application, and vice versa. A
        // lapsed or deactivated grant at the same scope is no duplicate; the write
        // below restores its row.
        var existing = await _userRepository.GetUserPermissionAsync(
            request.UserId, request.PermissionId, scope, cancellationToken);
        if (existing is not null && existing.IsValid())
        {
            return PermissionErrors.PermissionAlreadyGranted;
        }

        // While platform administrators must use two-step verification, platform
        // authority goes only to an account that already has its factor.
        var factorReady = await _factorGuard.EnsureAccountMayReceiveAsync(
            request.UserId,
            atPlatformScope: scope is null,
            [permission],
            cancellationToken);
        if (factorReady.IsError)
        {
            return factorReady.Errors;
        }

        // Every check is done. A grant inside an application carries access to it,
        // or the user holds a permission a Restricted application never lets them use.
        var accessWritten = application is not null
            && await _accessWriter.EnsureAsync(
                application, request.UserId, request.GrantedBy, request.ExpiresAt, cancellationToken);

        var userPermission = UserPermission.Create(
            request.UserId,
            request.PermissionId,
            request.GrantedBy,
            scope,
            request.ExpiresAt);

        await _userRepository.GrantPermissionAsync(userPermission, cancellationToken);

        _logger.LogInformation(
            "Permission granted to user: User {UserId}, Permission {PermissionId} ({PermissionCode}) at application {ApplicationId} by {GrantedBy}",
            request.UserId, request.PermissionId, permission.Code, scope, request.GrantedBy);

        // A direct grant leaves no trace in the role list, so this event is the
        // only record that the user's authority widened.
        await _publisher.Publish(
            new UserPermissionGrantedEvent(
                request.UserId,
                request.PermissionId,
                permission.Code.Value,
                scope,
                request.ExpiresAt,
                request.GrantedBy),
            cancellationToken);

        // After both writes, in the invitation's order: an audit handler that throws
        // must not stop the grant being written after its access was.
        if (accessWritten)
        {
            await _accessWriter.PublishGrantedAsync(
                application!, request.UserId, request.ExpiresAt, request.GrantedBy, cancellationToken);
        }

        return true;
    }
}
