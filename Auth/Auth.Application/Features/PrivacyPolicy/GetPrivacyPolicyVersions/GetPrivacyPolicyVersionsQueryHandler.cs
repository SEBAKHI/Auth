using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyVersions;

/// <summary>
/// Handler returning every recorded policy revision, newest first.
/// </summary>
public class GetPrivacyPolicyVersionsQueryHandler
    : IRequestHandler<GetPrivacyPolicyVersionsQuery, ErrorOr<IReadOnlyList<PrivacyPolicyVersionDto>>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;

    public GetPrivacyPolicyVersionsQueryHandler(IPrivacyPolicyVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<ErrorOr<IReadOnlyList<PrivacyPolicyVersionDto>>> Handle(
        GetPrivacyPolicyVersionsQuery request, CancellationToken cancellationToken)
    {
        var versions = await _repository.GetAllAsync(cancellationToken);

        var dtos = new List<PrivacyPolicyVersionDto>(versions.Count);
        foreach (var version in versions)
        {
            // Which languages are written drives the editor's completeness
            // indicator — a version missing translations must be visible as
            // incomplete before it is published.
            var translations = await _repository.GetTranslationsAsync(version.Id, cancellationToken);

            dtos.Add(new PrivacyPolicyVersionDto
            {
                Id = version.Id,
                Version = version.Version,
                EffectiveDateUtc = version.EffectiveDateUtc,
                IsPublished = version.IsPublished,
                NotifiedAtUtc = version.NotifiedAtUtc,
                NotifiedCount = version.NotifiedCount,
                CreatedAt = version.CreatedAt,
                Languages = translations.Select(t => t.LanguageCode).ToList()
            });
        }

        return ErrorOrFactory.From<IReadOnlyList<PrivacyPolicyVersionDto>>(dtos);
    }
}
