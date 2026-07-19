using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ApplicationEntity = Auth.Domain.Entities.Application;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.CreateApplication;

/// <summary>
/// Handler for creating a new application.
/// </summary>
public class CreateApplicationCommandHandler : IRequestHandler<CreateApplicationCommand, ErrorOr<ApplicationDto>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<CreateApplicationCommandHandler> _logger;

    public CreateApplicationCommandHandler(
        IApplicationRepository applicationRepository,
        ILogger<CreateApplicationCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationDto>> Handle(CreateApplicationCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate code
        if (await _applicationRepository.ExistsByCodeAsync(request.Code, cancellationToken))
        {
            return ApplicationErrors.DuplicateCode(request.Code);
        }

        // Create application
        var application = ApplicationEntity.Create(
            request.Code,
            request.Name,
            request.Description,
            request.BaseUrl,
            request.CreatedBy);

        // Set additional properties using reflection or update method
        // Since Application.Create sets defaults, we need to update with custom values
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
            request.CreatedBy,
            request.ReauthenticationMaxAgeMinutes);

        await _applicationRepository.CreateAsync(application, cancellationToken);

        _logger.LogInformation(
            "Application created: {ApplicationId} ({ApplicationCode}) by {CreatedBy}",
            application.Id, application.Code, request.CreatedBy);

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
            ReauthenticationMaxAgeMinutes = application.ReauthenticationMaxAgeMinutes,
            CreatedAt = application.CreatedAt,
            CreatedBy = application.CreatedBy,
            ModifiedAt = application.ModifiedAt,
            ModifiedBy = application.ModifiedBy
        };
    }
}
