using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationRoles;

/// <summary>
/// Handler for getting all roles for an application.
/// </summary>
public class GetApplicationRolesQueryHandler : IRequestHandler<GetApplicationRolesQuery, ErrorOr<IReadOnlyList<RoleDto>>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetApplicationRolesQueryHandler> _logger;

    public GetApplicationRolesQueryHandler(
        IApplicationRepository applicationRepository,
        ILogger<GetApplicationRolesQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<RoleDto>>> Handle(GetApplicationRolesQuery request, CancellationToken cancellationToken)
    {
        // Verify application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var roles = await _applicationRepository.GetRolesAsync(request.ApplicationId, cancellationToken);

        var dtos = roles.Select(role => new RoleDto
        {
            Id = role.Id,
            ApplicationId = role.ApplicationId,
            Code = role.Code,
            Name = role.Name,
            Description = role.Description,
            IsSystem = role.IsSystem,
            IsActive = role.IsActive,
            CreatedAt = role.CreatedAt,
            ModifiedAt = role.ModifiedAt,
            Permissions = [] // Could be populated if needed
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} roles for application {ApplicationId}",
            dtos.Count, request.ApplicationId);

        return dtos;
    }
}
