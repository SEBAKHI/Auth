using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.CreatePermission;

/// <summary>
/// Handler for creating a new permission.
/// </summary>
public class CreatePermissionCommandHandler : IRequestHandler<CreatePermissionCommand, ErrorOr<PermissionDto>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<CreatePermissionCommandHandler> _logger;

    public CreatePermissionCommandHandler(
        IPermissionRepository permissionRepository,
        IApplicationRepository applicationRepository,
        ILogger<CreatePermissionCommandHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PermissionDto>> Handle(CreatePermissionCommand request, CancellationToken cancellationToken)
    {
        // Verify application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        // Check for duplicate code within application
        var existingPermission = await _permissionRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (existingPermission != null && existingPermission.ApplicationId == request.ApplicationId)
        {
            return PermissionErrors.DuplicateCode(request.Code, request.ApplicationId);
        }

        // Verify parent permission exists if specified
        if (request.ParentId.HasValue)
        {
            var parentPermission = await _permissionRepository.GetByIdAsync(request.ParentId.Value, cancellationToken);
            if (parentPermission == null)
            {
                return PermissionErrors.NotFound(request.ParentId.Value);
            }
        }

        // Create permission
        var permission = Permission.Create(
            request.ApplicationId,
            request.Code,
            request.Name,
            request.Description,
            request.ParentId,
            request.CreatedBy);

        await _permissionRepository.CreateAsync(permission, cancellationToken);

        _logger.LogInformation(
            "Permission created: {PermissionId} ({PermissionCode}) for application {ApplicationId} by {CreatedBy}",
            permission.Id, permission.Code, request.ApplicationId, request.CreatedBy);

        return new PermissionDto
        {
            Id = permission.Id,
            ApplicationId = permission.ApplicationId,
            Code = permission.Code,
            Name = permission.Name,
            Description = permission.Description,
            ParentId = permission.ParentId,
            Level = permission.Level,
            IsWildcard = permission.IsWildcard,
            IsActive = permission.IsActive,
            CreatedAt = permission.CreatedAt,
            ModifiedAt = permission.ModifiedAt
        };
    }
}
