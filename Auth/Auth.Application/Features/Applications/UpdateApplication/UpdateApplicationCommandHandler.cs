using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.UpdateApplication;

/// <summary>
/// Handler for updating an existing application.
/// </summary>
public class UpdateApplicationCommandHandler : IRequestHandler<UpdateApplicationCommand, ErrorOr<ApplicationDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<UpdateApplicationCommandHandler> _logger;

    public UpdateApplicationCommandHandler(
        IApplicationRepository applicationRepository,
        ILogger<UpdateApplicationCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationDto>> Handle(UpdateApplicationCommand request, CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.Id, cancellationToken);

        if (application == null)
        {
            return ApplicationErrors.NotFound(request.Id);
        }

        // Update application
        application.Update(
            request.Name,
            request.Description,
            request.BaseUrl,
            request.LogoUrl,
            request.ContactEmail,
            request.AllowSelfRegistration,
            request.RequireTwoFactor,
            request.RequireEmailVerification,
            request.SessionTimeoutMinutes,
            request.MaxConcurrentSessions,
            request.ModifiedBy);

        // Null means "leave the allowlist untouched"; an empty list clears it.
        if (request.RedirectUris is not null)
        {
            application.SetRedirectUris(request.RedirectUris, request.ModifiedBy);
        }

        await _applicationRepository.UpdateAsync(application, cancellationToken);

        _logger.LogInformation(
            "Application updated: {ApplicationId} ({ApplicationCode}) by {ModifiedBy}",
            application.Id, application.Code, request.ModifiedBy);

        return new ApplicationDto
        {
            Id = application.Id,
            Code = application.Code,
            Name = application.Name,
            Description = application.Description,
            BaseUrl = application.BaseUrl,
            LogoUrl = application.LogoUrl,
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
            ModifiedAt = application.ModifiedAt,
            ModifiedBy = application.ModifiedBy
        };
    }
}
