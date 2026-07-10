using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationOrganizations;

/// <summary>
/// Handler for getting paginated organizations that have an application enabled.
/// </summary>
public class GetApplicationOrganizationsQueryHandler : IRequestHandler<GetApplicationOrganizationsQuery, ErrorOr<PagedApplicationOrganizationsDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetApplicationOrganizationsQueryHandler> _logger;

    public GetApplicationOrganizationsQueryHandler(
        IApplicationRepository applicationRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetApplicationOrganizationsQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedApplicationOrganizationsDto>> Handle(
        GetApplicationOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        // Verify application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var (organizations, totalCount) = await _applicationRepository.GetOrganizationsPagedAsync(
            request.ApplicationId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var dtos = organizations.Select(organization => new ApplicationOrganizationDto
        {
            OrganizationId = organization.OrganizationId,
            Code = organization.Code,
            Name = organization.Name,
            LogoUrl = _imageUrlComposer.Compose(organization.LogoUrl),
            OrganizationIsActive = organization.OrganizationIsActive,
            IsActive = organization.LinkIsActive,
            EnabledAt = organization.EnabledAt,
            ExpiresAt = organization.ExpiresAt,
            MemberCount = organization.MemberCount
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} of {Total} organizations for application {ApplicationId}",
            dtos.Count, totalCount, request.ApplicationId);

        return new PagedApplicationOrganizationsDto
        {
            Organizations = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
