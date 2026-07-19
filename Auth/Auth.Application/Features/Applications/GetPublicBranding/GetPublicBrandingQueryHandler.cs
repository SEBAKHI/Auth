using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetPublicBranding;

/// <summary>
/// Handles the public branding lookup. Unknown and inactive applications are
/// indistinguishable (both 404) so the anonymous endpoint cannot be used to
/// probe the application catalog.
/// </summary>
public class GetPublicBrandingQueryHandler
    : IRequestHandler<GetPublicBrandingQuery, ErrorOr<PublicBrandingDto>>
{
    private readonly IApplicationRepository _applicationRepository;

    public GetPublicBrandingQueryHandler(IApplicationRepository applicationRepository)
    {
        _applicationRepository = applicationRepository;
    }

    public async Task<ErrorOr<PublicBrandingDto>> Handle(
        GetPublicBrandingQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return ApplicationErrors.NotFoundByCode(request.ClientId ?? string.Empty);
        }

        var application = await _applicationRepository.GetByCodeAsync(request.ClientId, cancellationToken);
        if (application is null || !application.IsActive)
        {
            return ApplicationErrors.NotFoundByCode(request.ClientId);
        }

        return new PublicBrandingDto
        {
            Name = application.Name,
            LogoUrl = application.LogoUrl
        };
    }
}
