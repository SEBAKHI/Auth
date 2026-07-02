using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissionImplications;

/// <summary>
/// Handler for getting permissions implied by a permission.
/// </summary>
public class GetPermissionImplicationsQueryHandler : IRequestHandler<GetPermissionImplicationsQuery, ErrorOr<IReadOnlyList<PermissionDto>>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<GetPermissionImplicationsQueryHandler> _logger;

    public GetPermissionImplicationsQueryHandler(
        IPermissionRepository permissionRepository,
        ILogger<GetPermissionImplicationsQueryHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<PermissionDto>>> Handle(GetPermissionImplicationsQuery request, CancellationToken cancellationToken)
    {
        // Verify the permission exists
        var permission = await _permissionRepository.GetByIdAsync(request.PermissionId, cancellationToken);
        if (permission == null)
        {
            return PermissionErrors.NotFound(request.PermissionId);
        }

        var implications = await _permissionRepository.GetImplicationsAsync(request.PermissionId, cancellationToken);

        // Fetch the actual Permission entities for each implication
        var dtos = new List<PermissionDto>();
        foreach (var implication in implications)
        {
            var impliedPermission = await _permissionRepository.GetByIdAsync(implication.ImpliedPermissionId, cancellationToken);
            if (impliedPermission != null)
            {
                dtos.Add(new PermissionDto
                {
                    Id = impliedPermission.Id,
                    ApplicationId = impliedPermission.ApplicationId,
                    Code = impliedPermission.Code,
                    Name = impliedPermission.Name,
                    Description = impliedPermission.Description,
                    ParentId = impliedPermission.ParentId,
                    Level = impliedPermission.Level,
                    IsWildcard = impliedPermission.IsWildcard,
                    IsActive = impliedPermission.IsActive,
                    CreatedAt = impliedPermission.CreatedAt,
                    ModifiedAt = impliedPermission.ModifiedAt
                });
            }
        }

        _logger.LogDebug(
            "Retrieved {Count} implications for permission {PermissionId}",
            dtos.Count, request.PermissionId);

        // Sort in memory: the list is assembled per-row above. The SQL has no
        // ORDER BY, so default to code for a deterministic order.
        return SortHelper
            .Apply(dtos, request.SortBy ?? SortFields.PermissionImplications.Code, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<PermissionDto, object?>> SortSelectors =
        SortHelper.Selectors<PermissionDto>(
            (SortFields.PermissionImplications.Name, dto => dto.Name),
            (SortFields.PermissionImplications.Code, dto => dto.Code),
            (SortFields.PermissionImplications.Level, dto => dto.Level));
}
