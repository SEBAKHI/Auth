using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
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
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<UpdateApplicationCommandHandler> _logger;

    public UpdateApplicationCommandHandler(
        IApplicationRepository applicationRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUserSessionRepository sessionRepository,
        IImageUrlComposer imageUrlComposer,
        ILogger<UpdateApplicationCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _sessionRepository = sessionRepository;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationDto>> Handle(UpdateApplicationCommand request, CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.Id, cancellationToken);

        if (application == null)
        {
            return ApplicationErrors.NotFound(request.Id);
        }

        // Read the mode BEFORE Update overwrites it: closing an application down
        // to its invitation list is what makes the two checks below necessary,
        // and after the call there is nothing left to compare against.
        var wasOpenToEveryone = application.AccessMode == ApplicationAccessMode.Everyone;
        var closingDown = wasOpenToEveryone && request.AccessMode == ApplicationAccessMode.Restricted;

        if (closingDown)
        {
            // A restricted application admits only the users on its own access
            // list, so it cannot have enabled organizations. Refused rather than
            // disabling them silently: several companies losing access deserves
            // a deliberate act, recorded per organization, not a side effect of
            // changing a dropdown.
            if (await _applicationRepository.HasActiveOrganizationsAsync(request.Id, cancellationToken))
            {
                _logger.LogWarning(
                    "Refused to restrict application {ApplicationId} ({ApplicationCode}): organizations still have it enabled",
                    application.Id, application.Code);

                return ApplicationErrors.CannotRestrictWithActiveOrganizations;
            }
        }

        // Update application. The client resends the composed absolute URL it
        // last read, so the logo is normalized back to its storage key —
        // otherwise the row stores a host-bound URL that breaks the moment the
        // public image base changes. External URLs pass through untouched.
        application.Update(
            request.Name,
            request.Description,
            request.BaseUrl,
            _imageUrlComposer.Decompose(request.LogoUrl),
            request.ContactEmail,
            request.AllowSelfRegistration,
            request.RequireTwoFactor,
            request.RequireEmailVerification,
            request.SessionTimeoutMinutes,
            request.MaxConcurrentSessions,
            request.AccessMode,
            request.ModifiedBy,
            request.ReauthenticationMaxAgeMinutes);

        // Null means "leave the allowlist untouched"; an empty list clears it.
        if (request.RedirectUris is not null)
        {
            application.SetRedirectUris(request.RedirectUris, request.ModifiedBy);
        }

        await _applicationRepository.UpdateAsync(application, cancellationToken);

        if (closingDown)
        {
            // Everyone who was signed in got there under the open policy, and
            // most of them are no longer entitled. Their refresh tokens and
            // sessions go now; already-issued access tokens survive until they
            // expire on their own (JwtSettings:AccessTokenLifetime).
            await _refreshTokenRepository.RevokeAllForApplicationAsync(
                application.Id, request.ModifiedBy, TokenRevocationReasons.ApplicationAccessRevoked, cancellationToken);
            await _sessionRepository.TerminateForApplicationAsync(
                application.Id, TokenRevocationReasons.ApplicationAccessRevoked, cancellationToken);

            _logger.LogInformation(
                "Application {ApplicationId} ({ApplicationCode}) restricted to invited users; its tokens and sessions were revoked",
                application.Id, application.Code);
        }

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
            LogoUrl = _imageUrlComposer.Compose(application.LogoUrl),
            ContactEmail = application.ContactEmail,
            IsActive = application.IsActive,
            AccessMode = application.AccessMode,
            AllowSelfRegistration = application.AllowSelfRegistration,
            RequireTwoFactor = application.RequireTwoFactor,
            RequireEmailVerification = application.RequireEmailVerification,
            SessionTimeoutMinutes = application.SessionTimeoutMinutes,
            MaxConcurrentSessions = application.MaxConcurrentSessions,
            ReauthenticationMaxAgeMinutes = application.ReauthenticationMaxAgeMinutes,
            RedirectUris = [.. application.RedirectUris],
            CreatedAt = application.CreatedAt,
            CreatedBy = application.CreatedBy,
            ModifiedAt = application.ModifiedAt,
            ModifiedBy = application.ModifiedBy
        };
    }
}
