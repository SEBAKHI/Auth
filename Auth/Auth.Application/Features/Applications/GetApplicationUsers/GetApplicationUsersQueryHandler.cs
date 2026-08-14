using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationUsers;

/// <summary>
/// Handler for getting paginated users under an application.
/// </summary>
public class GetApplicationUsersQueryHandler : IRequestHandler<GetApplicationUsersQuery, ErrorOr<PagedApplicationUsersDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetApplicationUsersQueryHandler> _logger;

    public GetApplicationUsersQueryHandler(
        IApplicationRepository applicationRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetApplicationUsersQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<PagedApplicationUsersDto>> Handle(
        GetApplicationUsersQuery request,
        CancellationToken cancellationToken)
    {
        // Verify application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var (users, totalCount) = await _applicationRepository.GetUsersPagedAsync(
            request.ApplicationId,
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var dtos = users.Select(user => new ApplicationUserDto
        {
            UserId = user.UserId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            DisplayName = user.DisplayName,
            ProfileImageUrl = _imageUrlComposer.Compose(user.ProfileImageUrl),
            Status = user.Status,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            RoleNames = user.RoleNames,
            AccessSource = DescribeSource(user)
        }).ToList();

        _logger.LogDebug(
            "Retrieved {Count} of {Total} users for application {ApplicationId}",
            dtos.Count, totalCount, request.ApplicationId);

        return new PagedApplicationUsersDto
        {
            Users = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    private static string DescribeSource(Auth.Domain.ReadModels.Access.ApplicationUserRow row)
    {
        var sources = 0;
        if (row.ViaGrant) sources++;
        if (row.ViaDirect) sources++;
        if (row.ViaOrganization) sources++;

        if (sources > 1) return "multiple";
        if (row.ViaGrant) return "grant";
        if (row.ViaDirect) return "direct";
        return "organization";
    }
}
