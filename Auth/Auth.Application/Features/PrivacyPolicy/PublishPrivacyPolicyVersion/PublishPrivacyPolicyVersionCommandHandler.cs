using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;

/// <summary>
/// Handler publishing a revision. Publishing requires the neutral-language
/// document to exist — otherwise the public page would have nothing to fall
/// back to for visitors whose language is unwritten.
/// </summary>
public class PublishPrivacyPolicyVersionCommandHandler
    : IRequestHandler<PublishPrivacyPolicyVersionCommand, ErrorOr<Success>>
{
    private readonly IPrivacyPolicyVersionRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;

    public PublishPrivacyPolicyVersionCommandHandler(
        IPrivacyPolicyVersionRepository repository,
        IAuditLogRepository auditLogRepository)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<ErrorOr<Success>> Handle(
        PublishPrivacyPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await _repository.GetByVersionAsync(request.Version, cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NotFound(request.Version);
        }

        var fallback = await _repository.GetTranslationAsync(
            version.Id, PolicyLanguages.Fallback, cancellationToken);
        if (fallback is null)
        {
            return PrivacyPolicyErrors.InvalidContent(
                $"the '{PolicyLanguages.Fallback}' document must exist before publishing");
        }

        await _repository.PublishAsync(version.Id, cancellationToken);

        await _auditLogRepository.CreateAsync(
            AuditLog.CreateSuccess(
                actionType: "System",
                action: "system.privacy_policy_published",
                userId: request.RequestedBy,
                entityType: "PrivacyPolicyVersion",
                entityId: version.Id,
                additionalData: $"{{\"policyVersion\":\"{version.Version}\"}}"),
            cancellationToken);

        return Result.Success;
    }
}
