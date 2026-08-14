using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetAvailableApplications;

/// <summary>
/// Handler for the organization's enablement picker.
/// </summary>
public class GetAvailableApplicationsQueryHandler
    : IRequestHandler<GetAvailableApplicationsQuery, ErrorOr<IReadOnlyList<AvailableApplicationDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetAvailableApplicationsQueryHandler> _logger;

    public GetAvailableApplicationsQueryHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetAvailableApplicationsQueryHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<AvailableApplicationDto>>> Handle(
        GetAvailableApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        var rows = await _applicationRepository.GetAvailableForOrganizationAsync(
            request.OrganizationId, cancellationToken);

        var dtos = rows.Select(row => new AvailableApplicationDto
        {
            ApplicationId = row.ApplicationId,
            Code = row.Code,
            Name = row.Name,
            LogoUrl = _imageUrlComposer.Compose(row.LogoUrl)
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} applications available to organization {OrganizationId}",
            dtos.Count, request.OrganizationId);

        return dtos;
    }
}
