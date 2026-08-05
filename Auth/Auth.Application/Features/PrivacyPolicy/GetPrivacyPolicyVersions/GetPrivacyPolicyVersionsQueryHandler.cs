using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyVersions;

/// <summary>
/// Handler returning every recorded policy revision, newest first.
/// </summary>
public class GetPrivacyPolicyVersionsQueryHandler
    : IRequestHandler<GetPrivacyPolicyVersionsQuery, ErrorOr<IReadOnlyList<PrivacyPolicyVersionDto>>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;
    private readonly AccountDeletionSettings _deletionSettings;
    private readonly DataControllerSettings _controller;

    public GetPrivacyPolicyVersionsQueryHandler(
        IPrivacyPolicyVersionRepository repository,
        IOptionsSnapshot<AccountDeletionSettings> deletionSettings,
        IOptionsSnapshot<DataControllerSettings> controller)
    {
        _repository = repository;
        _deletionSettings = deletionSettings.Value;
        _controller = controller.Value;
    }

    public async Task<ErrorOr<IReadOnlyList<PrivacyPolicyVersionDto>>> Handle(
        GetPrivacyPolicyVersionsQuery request, CancellationToken cancellationToken)
    {
        var versions = await _repository.GetAllAsync(cancellationToken);
        var liveDisclosure = JsonSerializer.Serialize(
            GetPublishedPrivacyPolicyQueryHandler.BuildDisclosure(_deletionSettings, _controller));

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
                ChangeNote = version.ChangeNote,
                NotifiedAtUtc = version.NotifiedAtUtc,
                NotifiedCount = version.NotifiedCount,
                CreatedAt = version.CreatedAt,
                Languages = translations.Select(t => t.LanguageCode).ToList(),
                DisclosureOutOfDate = version.IsPublished
                    && await IsDisclosureStaleAsync(version.Id, liveDisclosure, cancellationToken)
            });
        }

        return ErrorOrFactory.From<IReadOnlyList<PrivacyPolicyVersionDto>>(dtos);
    }

    /// <summary>
    /// Whether the published document still describes the running system.
    ///
    /// Reported, never repaired. Re-rendering a published legal notice because a
    /// setting moved would change the text people were shown without a version
    /// or an effective date to mark it — a silent amendment. So the operator is
    /// told, and re-publishing stays their decision.
    ///
    /// The neutral document is enough to test: every language of a revision is
    /// rendered from the same disclosure in the same publish.
    /// </summary>
    private async Task<bool> IsDisclosureStaleAsync(
        Guid versionId, string liveDisclosure, CancellationToken cancellationToken)
    {
        var artifact = await _repository.GetArtifactAsync(
            versionId, PolicyLanguages.Fallback, cancellationToken);

        // No artifact means this revision predates publish-time rendering; it is
        // not stale, it is simply not rendered yet, and re-publishing fixes it.
        return artifact is not null
            && !string.Equals(artifact.DisclosureJson, liveDisclosure, StringComparison.Ordinal);
    }
}
