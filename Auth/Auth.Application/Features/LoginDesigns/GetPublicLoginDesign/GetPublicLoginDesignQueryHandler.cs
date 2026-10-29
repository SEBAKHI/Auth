using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.GetPublicLoginDesign;

/// <summary>Handles <see cref="GetPublicLoginDesignQuery"/>.</summary>
public sealed class GetPublicLoginDesignQueryHandler
    : IRequestHandler<GetPublicLoginDesignQuery, ErrorOr<PublicLoginDesignDto>>
{
    private readonly IApplicationRepository _applications;
    private readonly EffectiveLoginDesign _effective;
    private readonly LoginDesignRenderer _renderer;
    private readonly ILoginDesignRenderCache _rendered;

    public GetPublicLoginDesignQueryHandler(
        IApplicationRepository applications,
        EffectiveLoginDesign effective,
        LoginDesignRenderer renderer,
        ILoginDesignRenderCache rendered)
    {
        _applications = applications;
        _effective = effective;
        _renderer = renderer;
        _rendered = rendered;
    }

    public async Task<ErrorOr<PublicLoginDesignDto>> Handle(
        GetPublicLoginDesignQuery request,
        CancellationToken cancellationToken)
    {
        LoginDesignAudience? audience;
        Guid? applicationId = null;
        if (request.ClientId is null)
        {
            audience = await _renderer.AudienceAsync(null, cancellationToken);
        }
        else
        {
            // Unknown and inactive answer alike, so this anonymous endpoint cannot probe the
            // application catalogue (GetPublicBrandingQueryHandler).
            var application = string.IsNullOrWhiteSpace(request.ClientId)
                ? null
                : await _applications.GetByCodeAsync(request.ClientId, cancellationToken);
            if (application is null || !application.IsActive)
            {
                return ApplicationErrors.NotFoundByCode(request.ClientId);
            }

            applicationId = application.Id;
            audience = await _renderer.AudienceOfAsync(application, cancellationToken);
        }

        var design = await _effective.ForAsync(applicationId, cancellationToken);
        if (design is null || audience is null)
        {
            return LoginDesignErrors.NoneActive;
        }

        var language = Languages.Normalize(request.Language) ?? Languages.Default;
        var key = new LoginDesignRenderKey(
            design.Id, design.ContentHash, language, audience.ApplicationName, audience.PlatformName);
        var html = _rendered.GetOrRender(
            key, () => _renderer.Render(design.Content, design.StringsJson, audience, language));
        if (html.IsError)
        {
            return html.Errors;
        }

        return new PublicLoginDesignDto
        {
            Id = design.Id,
            ContentHash = design.ContentHash,
            Language = language,
            Dir = Languages.GetDirection(language),
            Html = html.Value
        };
    }
}
