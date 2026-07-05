using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoleApplications;

/// <summary>
/// Handler for getting the applications related to a role.
/// </summary>
public class GetRoleApplicationsQueryHandler : IRequestHandler<GetRoleApplicationsQuery, ErrorOr<IReadOnlyList<RoleApplicationDto>>>
{
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<GetRoleApplicationsQueryHandler> _logger;

    public GetRoleApplicationsQueryHandler(
        IRoleRepository roleRepository,
        ILogger<GetRoleApplicationsQueryHandler> logger)
    {
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<RoleApplicationDto>>> Handle(
        GetRoleApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        // Verify role exists
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
        {
            return RoleErrors.NotFound(request.RoleId);
        }

        var applications = await _roleRepository.GetRoleApplicationsAsync(request.RoleId, cancellationToken);

        var dtos = applications.Select(application => new RoleApplicationDto
        {
            ApplicationId = application.ApplicationId,
            Code = application.Code,
            Name = application.Name,
            LogoUrl = application.LogoUrl,
            IsActive = application.IsActive,
            Relationship = application switch
            {
                { IsOwner: true, IsAssigned: true } => "both",
                { IsOwner: true } => "owner",
                _ => "assigned"
            }
        }).ToList();

        _logger.LogDebug("Retrieved {Count} applications for role {RoleId}", dtos.Count, request.RoleId);

        // Sort in memory: the list is a small computed aggregate and the SQL
        // has no ORDER BY, so default to name for a deterministic order.
        return SortHelper
            .Apply(dtos, request.SortBy ?? SortFields.RoleApplications.Name, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<RoleApplicationDto, object?>> SortSelectors =
        SortHelper.Selectors<RoleApplicationDto>(
            (SortFields.RoleApplications.Name, dto => dto.Name),
            (SortFields.RoleApplications.Code, dto => dto.Code),
            (SortFields.RoleApplications.IsActive, dto => dto.IsActive),
            (SortFields.RoleApplications.Relationship, dto => dto.Relationship));
}
