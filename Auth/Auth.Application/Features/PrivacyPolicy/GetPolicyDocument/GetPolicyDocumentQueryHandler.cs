using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPolicyDocument;

/// <summary>
/// Serves a stored policy document.
///
/// The whole read path is a lookup: no template, no settings, no interpolation
/// and no fallback content of any kind. Either the bytes an operator published
/// exist, or the answer is 404 — a privacy notice that a system improvises is
/// worse than one it admits it cannot produce.
/// </summary>
public class GetPolicyDocumentQueryHandler
    : IRequestHandler<GetPolicyDocumentQuery, ErrorOr<PolicyDocumentDto>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;
    private readonly IPolicyArtifactCache _cache;

    public GetPolicyDocumentQueryHandler(
        IPrivacyPolicyVersionRepository repository,
        IPolicyArtifactCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<ErrorOr<PolicyDocumentDto>> Handle(
        GetPolicyDocumentQuery request, CancellationToken cancellationToken)
    {
        var language = PolicyLanguages.Normalize(request.LanguageCode);
        if (language is null)
        {
            return PrivacyPolicyErrors.UnsupportedLanguage(request.LanguageCode);
        }

        var artifact = request.Version is null
            ? await GetPublishedAsync(language, cancellationToken)
            : await GetArchivedAsync(request.Version, language, cancellationToken);

        if (artifact is null)
        {
            return PrivacyPolicyErrors.NoPublishedVersion;
        }

        return new PolicyDocumentDto
        {
            Html = artifact.Html,
            ContentHash = artifact.ContentHash,
            LanguageCode = artifact.LanguageCode,
            SourceLanguageCode = artifact.SourceLanguageCode,
            RenderedAt = artifact.RenderedAt
        };
    }

    private async Task<Domain.Entities.PrivacyPolicyArtifact?> GetPublishedAsync(
        string language, CancellationToken cancellationToken)
    {
        var cached = _cache.GetPublished(language);
        if (cached is not null) return cached;

        var artifact = await _repository.GetPublishedArtifactAsync(language, cancellationToken);
        if (artifact is not null) _cache.SetPublished(language, artifact);

        return artifact;
    }

    /// <summary>
    /// Archived revisions are addressed by their own URL and are immutable, so
    /// they are read straight through: caching a page nobody routinely requests
    /// would trade memory for nothing.
    /// </summary>
    private async Task<Domain.Entities.PrivacyPolicyArtifact?> GetArchivedAsync(
        string version, string language, CancellationToken cancellationToken)
    {
        var revision = await _repository.GetByVersionAsync(version, cancellationToken);
        return revision is null
            ? null
            : await _repository.GetArtifactAsync(revision.Id, language, cancellationToken);
    }
}
