using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplications;

/// <summary>
/// Handler for getting a paginated list of applications.
/// </summary>
public class GetApplicationsQueryHandler : IRequestHandler<GetApplicationsQuery, ErrorOr<PagedApplicationsDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<GetApplicationsQueryHandler> _logger;

    public GetApplicationsQueryHandler(
        IApplicationRepository applicationRepository,
        ILogger<GetApplicationsQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedApplicationsDto>> Handle(GetApplicationsQuery request, CancellationToken cancellationToken)
    {
        var (applications, totalCount) = await _applicationRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.Search,
            request.IsActive,
            cancellationToken);

        var dtos = applications.Select(app => new ApplicationDto
        {
            Id = app.Id,
            Code = app.Code,
            Name = app.Name,
            Description = app.Description,
            BaseUrl = app.BaseUrl,
            LogoUrl = app.LogoUrl,
            ContactEmail = app.ContactEmail,
            IsActive = app.IsActive,
            AllowSelfRegistration = app.AllowSelfRegistration,
            RequireTwoFactor = app.RequireTwoFactor,
            RequireEmailVerification = app.RequireEmailVerification,
            SessionTimeoutMinutes = app.SessionTimeoutMinutes,
            MaxConcurrentSessions = app.MaxConcurrentSessions,
            CreatedAt = app.CreatedAt,
            CreatedBy = app.CreatedBy,
            ModifiedAt = app.ModifiedAt,
            ModifiedBy = app.ModifiedBy
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} applications (page {Page} of {TotalPages})",
            dtos.Count, request.PageNumber, (int)Math.Ceiling(totalCount / (double)request.PageSize));

        return new PagedApplicationsDto
        {
            Applications = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
