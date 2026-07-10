using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplications;

/// <summary>
/// Handler for getting a paginated list of applications.
/// </summary>
public class GetApplicationsQueryHandler : IRequestHandler<GetApplicationsQuery, ErrorOr<PagedApplicationsDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetApplicationsQueryHandler> _logger;

    public GetApplicationsQueryHandler(
        IApplicationRepository applicationRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetApplicationsQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedApplicationsDto>> Handle(GetApplicationsQuery request, CancellationToken cancellationToken)
    {
        var (applications, totalCount) = await _applicationRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.Search,
            request.IsActive,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            applications.SelectMany(app => new Guid?[] { app.CreatedBy, app.ModifiedBy }),
            cancellationToken);

        var dtos = applications.Select(app => new ApplicationDto
        {
            Id = app.Id,
            Code = app.Code,
            Name = app.Name,
            Description = app.Description,
            BaseUrl = app.BaseUrl,
            LogoUrl = _imageUrlComposer.Compose(app.LogoUrl),
            ContactEmail = app.ContactEmail,
            IsActive = app.IsActive,
            AllowSelfRegistration = app.AllowSelfRegistration,
            RequireTwoFactor = app.RequireTwoFactor,
            RequireEmailVerification = app.RequireEmailVerification,
            SessionTimeoutMinutes = app.SessionTimeoutMinutes,
            MaxConcurrentSessions = app.MaxConcurrentSessions,
            CreatedAt = app.CreatedAt,
            CreatedBy = app.CreatedBy,
            CreatedByName = userNames.GetValueOrDefault(app.CreatedBy),
            ModifiedAt = app.ModifiedAt,
            ModifiedBy = app.ModifiedBy,
            ModifiedByName = app.ModifiedBy.HasValue
                ? userNames.GetValueOrDefault(app.ModifiedBy.Value)
                : null
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
