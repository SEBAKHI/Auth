using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

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
    private readonly DataControllerSettings _controller;

    public PublishPrivacyPolicyVersionCommandHandler(
        IPrivacyPolicyVersionRepository repository,
        IAuditLogRepository auditLogRepository,
        IOptionsSnapshot<DataControllerSettings> controller)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _controller = controller.Value;
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
