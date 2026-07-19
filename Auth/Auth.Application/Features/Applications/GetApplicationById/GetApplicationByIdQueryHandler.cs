using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationById;

/// <summary>
/// Handler for getting an application by ID.
/// </summary>
public class GetApplicationByIdQueryHandler : IRequestHandler<GetApplicationByIdQuery, ErrorOr<ApplicationDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetApplicationByIdQueryHandler> _logger;

    public GetApplicationByIdQueryHandler(
        IApplicationRepository applicationRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetApplicationByIdQueryHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationDto>> Handle(GetApplicationByIdQuery request, CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.Id, cancellationToken);

        if (application == null)
        {
            return ApplicationErrors.NotFound(request.Id);
        }

        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            [application.CreatedBy, application.ModifiedBy],
            cancellationToken);

        _logger.LogDebug("Retrieved application {ApplicationId} ({ApplicationCode})", application.Id, application.Code);

        return new ApplicationDto
        {
            Id = application.Id,
            Code = application.Code,
            Name = application.Name,
            Description = application.Description,
            BaseUrl = application.BaseUrl,
            LogoUrl = _imageUrlComposer.Compose(application.LogoUrl),
            ContactEmail = application.ContactEmail,
            IsActive = application.IsActive,
            AllowSelfRegistration = application.AllowSelfRegistration,
            RequireTwoFactor = application.RequireTwoFactor,
            RequireEmailVerification = application.RequireEmailVerification,
            SessionTimeoutMinutes = application.SessionTimeoutMinutes,
            MaxConcurrentSessions = application.MaxConcurrentSessions,
            RedirectUris = [.. application.RedirectUris],
            CreatedAt = application.CreatedAt,
            CreatedBy = application.CreatedBy,
            CreatedByName = userNames.GetValueOrDefault(application.CreatedBy),
            ModifiedAt = application.ModifiedAt,
            ModifiedBy = application.ModifiedBy,
            ModifiedByName = application.ModifiedBy.HasValue
                ? userNames.GetValueOrDefault(application.ModifiedBy.Value)
                : null
        };
    }
}
