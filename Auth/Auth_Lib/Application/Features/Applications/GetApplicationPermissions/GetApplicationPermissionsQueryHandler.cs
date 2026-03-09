using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Applications.GetApplicationPermissions;

/// <summary>
/// Handler for getting all permissions for an application.
/// </summary>
public class GetApplicationPermissionsQueryHandler : IRequestHandler<GetApplicationPermissionsQuery, ErrorOr<IReadOnlyList<PermissionDto>>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetApplicationPermissionsQueryHandler> _logger;

    public GetApplicationPermissionsQueryHandler(
        IApplicationRepository applicationRepository,
        ILogger<GetApplicationPermissionsQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<PermissionDto>>> Handle(GetApplicationPermissionsQuery request, CancellationToken cancellationToken)
    {
        // Verify application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var permissions = await _applicationRepository.GetPermissionsAsync(request.ApplicationId, cancellationToken);

        var dtos = permissions.Select(permission => new PermissionDto
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
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} permissions for application {ApplicationId}",
            dtos.Count, request.ApplicationId);

        return dtos;
    }
}
