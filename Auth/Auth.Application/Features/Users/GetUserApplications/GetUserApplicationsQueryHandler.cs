using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserApplications;

/// <summary>
/// Handler for getting all applications a user has access to.
/// </summary>
public class GetUserApplicationsQueryHandler : IRequestHandler<GetUserApplicationsQuery, ErrorOr<IReadOnlyList<UserApplicationDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetUserApplicationsQueryHandler> _logger;

    public GetUserApplicationsQueryHandler(
        IUserRepository userRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetUserApplicationsQueryHandler> logger)
    {
        _userRepository = userRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<UserApplicationDto>>> Handle(
        GetUserApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        // Verify user exists
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // Answered by the same repository the sign-in gate asks, so this report
        // can never claim access the gate would refuse.
        var accesses = await _applicationAccessRepository.GetApplicationsForUserAsync(
            request.UserId, cancellationToken);

        var dtos = accesses.Select(access => new UserApplicationDto
        {
            ApplicationId = access.ApplicationId,
            Code = access.Code,
            Name = access.Name,
            LogoUrl = _imageUrlComposer.Compose(access.LogoUrl),
            IsActive = access.IsActive,
            AccessSource = access switch
            {
                { ViaOpenAccess: true, ViaGrant: true } => "both",
                { ViaOpenAccess: true } => "open",
                _ => "grant"
            }
        }).ToList();

        _logger.LogDebug("Retrieved {Count} applications for user {UserId}", dtos.Count, request.UserId);

        // Sort in memory: the list is a small computed aggregate and the SQL
        // has no ORDER BY, so default to name for a deterministic order.
        return SortHelper
            .Apply(dtos, request.SortBy ?? SortFields.UserApplications.Name, request.SortDirection, SortSelectors)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, Func<UserApplicationDto, object?>> SortSelectors =
        SortHelper.Selectors<UserApplicationDto>(
            (SortFields.UserApplications.Name, dto => dto.Name),
            (SortFields.UserApplications.Code, dto => dto.Code),
            (SortFields.UserApplications.IsActive, dto => dto.IsActive),
            (SortFields.UserApplications.AccessSource, dto => dto.AccessSource));
}
