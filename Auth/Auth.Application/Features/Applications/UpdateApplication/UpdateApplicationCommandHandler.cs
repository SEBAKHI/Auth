using Auth.Application.Common;
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
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly OrganizationCreatorRoleCheck _creatorRoleCheck;
    private readonly PermissionGrantGuard _grantGuard;
    private readonly ImageReferenceGuard _imageReferenceGuard;
    private readonly ILogger<UpdateApplicationCommandHandler> _logger;

    public UpdateApplicationCommandHandler(
        IApplicationRepository applicationRepository,
        ICredentialRevocationService credentialRevocation,
        IImageUrlComposer imageUrlComposer,
        OrganizationCreatorRoleCheck creatorRoleCheck,
        PermissionGrantGuard grantGuard,
        ImageReferenceGuard imageReferenceGuard,
        ILogger<UpdateApplicationCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _credentialRevocation = credentialRevocation;
        _imageUrlComposer = imageUrlComposer;
        _creatorRoleCheck = creatorRoleCheck;
        _grantGuard = grantGuard;
        _imageReferenceGuard = imageReferenceGuard;
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

        // Read before Update overwrites it: an unchanged logo needs no claim.
        var storedLogoUrl = application.LogoUrl;
        var storedLogoUrlDark = application.LogoUrlDark;

        // The dark-mode logo arrived after this contract was published, so a
        // client that does not know it must not strip it: null leaves it as it
        // is, and the empty string removes it.
        var requestedLogoUrlDark = request.LogoUrlDark switch
        {
            null => storedLogoUrlDark,
            "" => null,
            var value => value,
        };

        // Update application. The client resends the composed absolute URL it
        // last read, so the logo is normalized back to its storage key —
        // otherwise the row stores a host-bound URL that breaks the moment the
        // public image base changes. External URLs pass through untouched.
        application.Update(
            request.Name,
            request.Description,
            request.BaseUrl,
            _imageUrlComposer.Decompose(request.LogoUrl),
            _imageUrlComposer.Decompose(requestedLogoUrlDark),
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

        // Same rule for the allowed scopes: null leaves them as they are, so a
        // client that does not know the field cannot strip them; an empty list
        // clears them to openid only. Narrowing takes effect at each session's
        // next refresh; widening at its next authorize.
        if (request.AllowedScopes is not null)
        {
            var scopes = application.SetAllowedScopes(request.AllowedScopes, request.ModifiedBy);
            if (scopes.IsError)
            {
                return scopes.Errors;
            }
        }

        var organizationCreation = await ApplyOrganizationCreationAsync(application, request, cancellationToken);
        if (organizationCreation.IsError)
        {
            return organizationCreation.Errors;
        }

        // Last before the write, so a request refused above claims nothing.
        // Each slot is checked against its own stored value: an unchanged slot
        // needs no claim, a new upload key must be the actor's.
        foreach (var (incoming, stored) in new[]
                 {
                     (request.LogoUrl, storedLogoUrl),
                     (requestedLogoUrlDark, storedLogoUrlDark),
                 })
        {
            var logo = await _imageReferenceGuard.EnsureCanStoreAsync(
                incoming, stored, request.ModifiedBy, cancellationToken);
            if (logo.IsError)
            {
                return logo.Errors;
            }
        }

        await _applicationRepository.UpdateAsync(application, cancellationToken);

        if (closingDown)
        {
            // Everyone who was signed in got there under the open policy, and
            // most of them are no longer entitled. Every session of the
            // application ends now, its refresh tokens with it, and each ended
            // session's id is blacklisted, so the access tokens already out are
            // refused from the next request. Ending the rows without that would
            // also disarm a later switch-off, which finds no open row to
            // blacklist. Not on the request's token: the application is saved.
            await _credentialRevocation.TerminateApplicationSessionsAsync(
                application.Id,
                userId: null,
                request.ModifiedBy,
                TokenRevocationReasons.ApplicationAccessRevoked,
                CancellationToken.None);

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
            LogoUrlDark = _imageUrlComposer.Compose(application.LogoUrlDark),
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
            AllowedScopes = [.. application.AllowedScopes.OptionalNames],
            AllowOrganizationCreation = application.AllowOrganizationCreation,
            OrganizationCreatorRoleId = application.OrganizationCreatorRoleId,
            CreatedAt = application.CreatedAt,
            CreatedBy = application.CreatedBy,
            ModifiedAt = application.ModifiedAt,
            ModifiedBy = application.ModifiedBy
        };
    }

    /// <summary>
    /// Applies the organization-creation settings. Null leaves each one as it is,
    /// because the logo upload re-sends the whole body without them.
    /// </summary>
    /// <remarks>
    /// Checked only when the effective configuration changes, so renaming an
    /// application that already allows creation does not re-test the
    /// administrator saving the rename. When creation ends up allowed, the role
    /// must be this application's, active and non-empty, and the administrator
    /// must hold every permission it carries: the organization-creation step
    /// grants this role with no guard of its own, so this save is where the
    /// authority for every later grant is checked. No amplification through
    /// configuration, the same rule as assigning the role directly.
    /// </remarks>
    private async Task<ErrorOr<Success>> ApplyOrganizationCreationAsync(
        Auth.Domain.Entities.Application application,
        UpdateApplicationCommand request,
        CancellationToken cancellationToken)
    {
        if (request.AllowOrganizationCreation is null && request.OrganizationCreatorRoleId is null)
        {
            return Result.Success;
        }

        var allow = request.AllowOrganizationCreation ?? application.AllowOrganizationCreation;
        var roleId = request.OrganizationCreatorRoleId is Guid requested
            ? requested == Guid.Empty ? null : requested
            : application.OrganizationCreatorRoleId;

        if (allow == application.AllowOrganizationCreation && roleId == application.OrganizationCreatorRoleId)
        {
            return Result.Success;
        }

        if (allow)
        {
            var codes = await _creatorRoleCheck.GetUsableCodesAsync(application.Id, roleId, cancellationToken);
            if (codes is null)
            {
                return ApplicationErrors.OrganizationCreatorRoleInvalid;
            }

            var canGrant = await _grantGuard.EnsureCanGrantAsync(request.ModifiedBy, codes, cancellationToken);
            if (canGrant.IsError)
            {
                _logger.LogWarning(
                    "Blocked creator role {RoleId} for application {ApplicationId}: actor {ModifiedBy} does not hold every permission the role carries",
                    roleId, application.Id, request.ModifiedBy);
                return canGrant.Errors;
            }
        }

        return application.SetOrganizationCreation(allow, roleId, request.ModifiedBy);
    }
}
