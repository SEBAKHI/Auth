using Auth.Application.DTOs;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.PreviewLoginDesign;

/// <summary>Handles <see cref="PreviewLoginDesignQuery"/>.</summary>
public sealed class PreviewLoginDesignQueryHandler
    : IRequestHandler<PreviewLoginDesignQuery, ErrorOr<LoginDesignPreviewDto>>
{
    private readonly LoginDesignRenderer _renderer;

    public PreviewLoginDesignQueryHandler(LoginDesignRenderer renderer)
    {
        _renderer = renderer;
    }

    public async Task<ErrorOr<LoginDesignPreviewDto>> Handle(
        PreviewLoginDesignQuery request,
        CancellationToken cancellationToken)
    {
        var audience = await _renderer.AudienceAsync(request.ApplicationId, cancellationToken);
        if (audience is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId!.Value);
        }

        var language = Languages.Normalize(request.Language) ?? Languages.Default;
        var strings = string.IsNullOrWhiteSpace(request.StringsJson) ? "{}" : request.StringsJson;
        var html = _renderer.Render(request.Content, strings, audience, language);
        if (html.IsError)
        {
            return html.Errors;
        }

        return new LoginDesignPreviewDto
        {
            Html = html.Value,
            Language = language,
            Dir = Languages.GetDirection(language),
            Warnings = LoginDesign.WarningsOf(request.Content)
        };
    }
}
