using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyContent;

/// <summary>
/// Handler returning one language document for editing. An unwritten language
/// yields empty content rather than an error, so the editor can start a new
/// translation from a blank document.
/// </summary>
public class GetPrivacyPolicyContentQueryHandler
    : IRequestHandler<GetPrivacyPolicyContentQuery, ErrorOr<PrivacyPolicyContentDto>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;

    public GetPrivacyPolicyContentQueryHandler(IPrivacyPolicyVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<PrivacyPolicyContentDto>> Handle(
        GetPrivacyPolicyContentQuery request, CancellationToken cancellationToken)
    {
        var language = PolicyLanguages.Normalize(request.LanguageCode);
        if (language is null)
        {
            return PrivacyPolicyErrors.UnsupportedLanguage(request.LanguageCode);
        }

        var version = await _repository.GetByVersionAsync(request.Version, cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NotFound(request.Version);
        }

        var translation = await _repository.GetTranslationAsync(
            version.Id, language, cancellationToken);

        return new PrivacyPolicyContentDto
        {
            Version = version.Version,
            LanguageCode = language,
            ContentJson = translation?.ContentJson ?? string.Empty,
            ModifiedAt = translation?.ModifiedAt
        };
    }
}
