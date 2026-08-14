using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationAccessGrants;

/// <summary>
/// Handler for reading an application's access list.
/// </summary>
public class GetApplicationAccessGrantsQueryHandler
    : IRequestHandler<GetApplicationAccessGrantsQuery, ErrorOr<IReadOnlyList<ApplicationAccessGrantDto>>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _accessRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetApplicationAccessGrantsQueryHandler> _logger;

    public GetApplicationAccessGrantsQueryHandler(
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository accessRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetApplicationAccessGrantsQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _accessRepository = accessRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<ApplicationAccessGrantDto>>> Handle(
        GetApplicationAccessGrantsQuery request,
        CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var grants = await _accessRepository.GetGrantsAsync(request.ApplicationId, cancellationToken);

        var dtos = grants.Select(grant => new ApplicationAccessGrantDto
        {
            UserId = grant.UserId,
            Email = grant.Email,
            FirstName = grant.FirstName,
            LastName = grant.LastName,
            FullName = $"{grant.FirstName} {grant.LastName}".Trim(),
            DisplayName = grant.DisplayName,
            ProfileImageUrl = _imageUrlComposer.Compose(grant.ProfileImageUrl),
            Status = grant.Status,
            GrantedAt = grant.GrantedAt,
            GrantedBy = grant.GrantedBy,
            GrantedByName = grant.GrantedByName,
            ExpiresAt = grant.ExpiresAt,
            Note = grant.Note
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} access grants for application {ApplicationId}",
            dtos.Count, request.ApplicationId);

        return dtos;
    }
}
