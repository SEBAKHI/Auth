using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserPermissions;

/// <summary>
/// Handler for getting all direct permissions granted to a user.
/// </summary>
public class GetUserPermissionsQueryHandler : IRequestHandler<GetUserPermissionsQuery, ErrorOr<IReadOnlyList<UserPermissionDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly AccountActionPolicy _accountActionPolicy;
    private readonly ILogger<GetUserPermissionsQueryHandler> _logger;

    public GetUserPermissionsQueryHandler(
        IUserRepository userRepository,
        IPermissionRepository permissionRepository,
        IApplicationRepository applicationRepository,
        AccountActionPolicy accountActionPolicy,
        ILogger<GetUserPermissionsQueryHandler> logger)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
        _applicationRepository = applicationRepository;
        _accountActionPolicy = accountActionPolicy;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<UserPermissionDto>>> Handle(GetUserPermissionsQuery request, CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var userPermissions = await _userRepository.GetUserPermissionsAsync(request.UserId, cancellationToken);
        var granterNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, userPermissions.Select(userPerm => (Guid?)userPerm.GrantedBy), cancellationToken);

        // Whether the caller may revoke each grant, by the rules the revocation
        // enforces, decided in memory from one set of reads.
        var decisions = request.ActorId is { } actorId
            ? await _accountActionPolicy.DecideForAsync(actorId, [request.UserId], cancellationToken)
            : null;

        // Enrich with permission and application names
        var dtos = new List<UserPermissionDto>();
        foreach (var userPerm in userPermissions)
        {
            var dto = new UserPermissionDto
            {
                Id = userPerm.Id,
                UserId = userPerm.UserId,
                PermissionId = userPerm.PermissionId,
                ApplicationId = userPerm.ApplicationId,
                CreatedAt = userPerm.GrantedAt,
                CreatedBy = userPerm.GrantedBy,
                CreatedByName = granterNames.GetValueOrDefault(userPerm.GrantedBy),
                ExpiresAt = userPerm.ExpiresAt,
                IsActive = userPerm.IsActive
            };

            // Get permission info
            var permission = await _permissionRepository.GetByIdAsync(userPerm.PermissionId, cancellationToken);
            if (permission != null)
            {
                dto.PermissionName = permission.Name;
                dto.PermissionCode = permission.Code;
                dto.PermissionDescription = permission.Description;
            }

            dto.CanRemove = decisions?.AllowsPermissionRevocation(
                request.UserId, dto.PermissionCode, userPerm.ApplicationId);

            // The application's name, even once it is deleted: without it the row
            // read like a platform-wide assignment. The flag says it is deleted.
            if (userPerm.ApplicationId.HasValue)
            {
                var app = await _applicationRepository.GetByIdIncludingDeletedAsync(userPerm.ApplicationId.Value, cancellationToken);
                if (app != null)
                {
                    dto.ApplicationName = app.Name;
                    dto.ApplicationCode = app.Code;
                    dto.ApplicationIsDeleted = app.IsDeleted;
                }
            }

            dtos.Add(dto);
        }

        _logger.LogDebug("Retrieved {Count} direct permissions for user {UserId}", dtos.Count, request.UserId);

        // Sort in memory: the sortable fields (permission/application name) are
        // enrichment values that don't exist as columns on [UserPermissions]. The
        // SQL has no ORDER BY, so default to grant date for a deterministic order.
        return SortHelper
            .Apply(dtos, request.SortBy ?? SortFields.UserPermissions.CreatedAt, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<UserPermissionDto, object?>> SortSelectors =
        SortHelper.Selectors<UserPermissionDto>(
            (SortFields.UserPermissions.PermissionName, dto => dto.PermissionName),
            (SortFields.UserPermissions.PermissionCode, dto => dto.PermissionCode),
            (SortFields.UserPermissions.PermissionDescription, dto => dto.PermissionDescription),
            (SortFields.UserPermissions.ApplicationName, dto => dto.ApplicationName),
            (SortFields.UserPermissions.ApplicationCode, dto => dto.ApplicationCode),
            (SortFields.UserPermissions.IsActive, dto => dto.IsActive),
            (SortFields.UserPermissions.ExpiresAt, dto => dto.ExpiresAt),
            (SortFields.UserPermissions.CreatedAt, dto => dto.CreatedAt));
}
