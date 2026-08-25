using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Constants;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.SavePrivacyPolicyContent;

/// <summary>
/// Handler saving one language document. The payload is validated as a
/// well-formed policy document before it is stored — a malformed save would
/// otherwise break the public page for every visitor in that language.
/// </summary>
public class SavePrivacyPolicyContentCommandHandler
    : IRequestHandler<SavePrivacyPolicyContentCommand, ErrorOr<PrivacyPolicyContentDto>>
{
    /// <summary>Top-level members the renderer requires.</summary>
    private static readonly string[] RequiredMembers =
    [
        "title", "effectiveDate", "versionLabel", "intro", "sections",
        "retention", "deletion", "rights", "closing"
    ];

    private readonly IPrivacyPolicyVersionRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;

    public SavePrivacyPolicyContentCommandHandler(
        IPrivacyPolicyVersionRepository repository,
        IAuditLogRepository auditLogRepository)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<ErrorOr<PrivacyPolicyContentDto>> Handle(
        SavePrivacyPolicyContentCommand request, CancellationToken cancellationToken)
    {
        var language = PolicyLanguages.Normalize(request.LanguageCode);
        if (language is null)
        {
            return PrivacyPolicyErrors.UnsupportedLanguage(request.LanguageCode);
        }

        var validation = ValidateDocument(request.ContentJson);
        if (validation is not null)
        {
            return PrivacyPolicyErrors.InvalidContent(validation);
        }

        var version = await _repository.GetByVersionAsync(request.Version, cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NotFound(request.Version);
        }

        var existing = await _repository.GetTranslationAsync(
            version.Id, language, cancellationToken);

        PrivacyPolicyTranslation translation;
        if (existing is null)
        {
            translation = PrivacyPolicyTranslation.Create(
                version.Id, language, request.ContentJson, request.RequestedBy);
        }
        else
        {
            existing.UpdateContent(request.ContentJson, request.RequestedBy);
            translation = existing;
        }

        await _repository.UpsertTranslationAsync(translation, cancellationToken);

        await _auditLogRepository.CreateAsync(
            AuditLog.CreateSuccess(
                actionType: AuditActionTypes.System,
                action: AuditActions.SystemPrivacyPolicyContentSaved,
                userId: request.RequestedBy,
                entityType: "PrivacyPolicyVersion",
                entityId: version.Id,
                additionalData:
                    $"{{\"policyVersion\":\"{version.Version}\",\"language\":\"{language}\"}}"),
            cancellationToken);

        return new PrivacyPolicyContentDto
        {
            Version = version.Version,
            LanguageCode = language,
            ContentJson = translation.ContentJson,
            ModifiedAt = translation.ModifiedAt
        };
    }

    /// <summary>Returns a reason when the document is unusable, else null.</summary>
    private static string? ValidateDocument(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return "content is empty";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contentJson);
        }
        catch (JsonException ex)
        {
            return $"not valid JSON ({ex.Message})";
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "the document must be a JSON object";
            }

            var missing = RequiredMembers
                .Where(member => !document.RootElement.TryGetProperty(member, out _))
                .ToList();

            return missing.Count > 0
                ? $"missing required section(s): {string.Join(", ", missing)}"
                : null;
        }
    }
}
