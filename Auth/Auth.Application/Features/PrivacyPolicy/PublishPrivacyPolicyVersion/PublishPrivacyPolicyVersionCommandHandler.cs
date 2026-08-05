using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;

/// <summary>
/// Handler publishing a revision.
///
/// Publishing is not a flag flip. It renders the served document for every
/// supported language, with the controller identity of this moment interpolated
/// in, and stores those bytes — so reading the policy afterwards touches no
/// template, no settings and no interpolation. That is what moves "a placeholder
/// could reach a reader" from unlikely to unreachable, and what keeps the notice
/// readable when everything upstream of the stored bytes is unavailable.
///
/// Rendering happens BEFORE the flag moves: a document that cannot be rendered
/// must leave the previous revision serving rather than publish a gap.
/// </summary>
public class PublishPrivacyPolicyVersionCommandHandler
    : IRequestHandler<PublishPrivacyPolicyVersionCommand, ErrorOr<Success>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IPolicyDocumentRenderer _renderer;
    private readonly IPolicyArtifactCache _cache;
    private readonly DataControllerSettings _controller;
    private readonly AccountDeletionSettings _deletionSettings;
    private readonly IdentityProviderSettings _idpSettings;

    public PublishPrivacyPolicyVersionCommandHandler(
        IPrivacyPolicyVersionRepository repository,
        IAuditLogRepository auditLogRepository,
        IPolicyDocumentRenderer renderer,
        IPolicyArtifactCache cache,
        IOptionsSnapshot<DataControllerSettings> controller,
        IOptionsSnapshot<AccountDeletionSettings> deletionSettings,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _renderer = renderer;
        _cache = cache;
        _controller = controller.Value;
        _deletionSettings = deletionSettings.Value;
        _idpSettings = idpSettings.Value;
    }

    public async Task<ErrorOr<Success>> Handle(
        PublishPrivacyPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await _repository.GetByVersionAsync(request.Version, cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NotFound(request.Version);
        }

        var translations = await _repository.GetTranslationsAsync(version.Id, cancellationToken);

        var fallback = translations.FirstOrDefault(t =>
            string.Equals(t.LanguageCode, PolicyLanguages.Fallback, StringComparison.OrdinalIgnoreCase));
        if (fallback is null)
        {
            return PrivacyPolicyErrors.InvalidContent(
                $"the '{PolicyLanguages.Fallback}' document must exist before publishing");
        }

        // A privacy policy that does not name its controller is not a valid
        // disclosure: KVKK Art. 10 and GDPR Art. 13(1)(a) both require the
        // controller's identity, and Art. 12(2) requires a reachable channel
        // for rights requests. This used to be guarded only by a banner in the
        // accounts SPA, which could not stop anything and did not even see the
        // published document — it tested a build-time constant.
        var missing = _controller.MissingRequired();
        if (missing.Count > 0)
        {
            return PrivacyPolicyErrors.ControllerDetailsIncomplete(string.Join(", ", missing));
        }

        // The deletion entry point is required by app-store data-deletion
        // policies, and it is an absolute link out of a script-less document —
        // publishing without an origin would publish a dead link.
        var accountsBaseUrl = _idpSettings.AccountsBaseUrl;
        if (string.IsNullOrWhiteSpace(accountsBaseUrl))
        {
            return PrivacyPolicyErrors.InvalidContent(
                "IdentityProvider:AccountsBaseUrl must be set before publishing: the policy's " +
                "account-deletion link is absolute and cannot be built without it");
        }

        var rendered = RenderAll(version, translations, fallback, accountsBaseUrl);
        if (rendered.IsError)
        {
            return rendered.Errors;
        }

        await _repository.ReplaceArtifactsAsync(version.Id, rendered.Value, cancellationToken);
        await _repository.PublishAsync(version.Id, cancellationToken);

        // Replaced, not evicted: the documents are already in hand, so no reader
        // after this point has to reach the database to be served correctly.
        _cache.ReplacePublished(rendered.Value);

        await _auditLogRepository.CreateAsync(
            AuditLog.CreateSuccess(
                actionType: "System",
                action: "system.privacy_policy_published",
                userId: request.RequestedBy,
                entityType: "PrivacyPolicyVersion",
                entityId: version.Id,
                additionalData: JsonSerializer.Serialize(new
                {
                    policyVersion = version.Version,
                    languages = rendered.Value.Count,
                    translated = rendered.Value.Count(a => !a.IsLanguageFallback)
                })),
            cancellationToken);

        return Result.Success;
    }

    /// <summary>
    /// Renders one document per supported language. An unwritten language is
    /// served the neutral document carrying a notice, in the reader's language,
    /// that says so — rather than being unreachable or silently English.
    /// </summary>
    private ErrorOr<List<PrivacyPolicyArtifact>> RenderAll(
        PrivacyPolicyVersion version,
        IReadOnlyList<PrivacyPolicyTranslation> translations,
        PrivacyPolicyTranslation fallback,
        string accountsBaseUrl)
    {
        var disclosure = GetPublishedPrivacyPolicyQueryHandler.BuildDisclosure(
            _deletionSettings, _controller);
        var disclosureJson = JsonSerializer.Serialize(disclosure);

        var artifacts = new List<PrivacyPolicyArtifact>(PolicyLanguages.Ordered.Count);

        foreach (var language in PolicyLanguages.Ordered)
        {
            var source = translations.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, language, StringComparison.OrdinalIgnoreCase))
                ?? fallback;

            var content = PolicyDocumentModel.TryParse(source.ContentJson);
            if (content is null)
            {
                return PrivacyPolicyErrors.InvalidContent(
                    $"the '{source.LanguageCode}' document is not readable JSON");
            }

            var isFallback = !string.Equals(
                source.LanguageCode, language, StringComparison.OrdinalIgnoreCase);

            var result = _renderer.Render(new PolicyRenderRequest(
                LanguageCode: language,
                Content: content,
                Disclosure: disclosure,
                Version: version.Version,
                AvailableLanguages: PolicyLanguages.Ordered,
                IsFallbackLanguage: isFallback,
                AccountsBaseUrl: accountsBaseUrl));

            if (result.IsError)
            {
                return result.Errors;
            }

            artifacts.Add(PrivacyPolicyArtifact.Create(
                version.Id,
                language,
                source.LanguageCode,
                result.Value.Html,
                result.Value.ContentHash,
                result.Value.StyleHash,
                disclosureJson));
        }

        return artifacts;
    }
}
