using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.PrivacyPolicy.NotifyPrivacyPolicyVersion;

/// <summary>
/// Handler fanning the policy-change notice out to every active, confirmed
/// account. Each recipient gets the notice in their own preferred language
/// (the notification pipeline resolves it from RecipientUserId). Individual
/// send failures are logged and skipped — one bad address must not stall a
/// compliance notification — and the revision is stamped with the delivered
/// count, which is the auditable record that the policy's "we notify you of
/// material changes" promise was kept.
/// </summary>
public class NotifyPrivacyPolicyVersionCommandHandler
    : IRequestHandler<NotifyPrivacyPolicyVersionCommand, ErrorOr<PrivacyPolicyNotifyResultDto>>
{
    private readonly IPrivacyPolicyVersionRepository _versionRepository;
    private readonly IUserRepository _userRepository;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<NotifyPrivacyPolicyVersionCommandHandler> _logger;

    public NotifyPrivacyPolicyVersionCommandHandler(
        IPrivacyPolicyVersionRepository versionRepository,
        IUserRepository userRepository,
        INotificationService notificationService,
        IAuditLogRepository auditLogRepository,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<NotifyPrivacyPolicyVersionCommandHandler> logger)
    {
        _versionRepository = versionRepository;
        _userRepository = userRepository;
        _notificationService = notificationService;
        _auditLogRepository = auditLogRepository;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<PrivacyPolicyNotifyResultDto>> Handle(
        NotifyPrivacyPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await _versionRepository.GetByVersionAsync(request.Version, cancellationToken);
        if (version is null)
        {
            return PrivacyPolicyErrors.NotFound(request.Version);
        }

        var recipients = await _userRepository.GetActiveNotificationRecipientsAsync(cancellationToken);
        var policyLink = _emailSettings.BuildFrontendUrl("/privacy");
        var delivered = 0;

        foreach (var recipient in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sendResult = await _notificationService.SendAsync(
                new NotificationRequest
                {
                    TypeCode = NotificationTypeCodes.PrivacyPolicyUpdated,
                    RecipientAddress = recipient.Email,
                    RecipientName = recipient.DisplayName ?? recipient.FirstName ?? "User",
                    RecipientUserId = recipient.Id,
                    Variables = new Dictionary<string, object?>
                    {
                        ["UserName"] = recipient.DisplayName ?? recipient.FirstName ?? "User",
                        ["PolicyVersion"] = version.Version,
                        ["EffectiveDate"] = version.EffectiveDateUtc.ToString("yyyy-MM-dd"),
                        ["PolicyLink"] = policyLink
                    }
                },
                cancellationToken);

            if (sendResult.IsError)
            {
                _logger.LogError(
                    "Policy-change notice for version {Version} failed for user {UserId}: {Error}",
                    version.Version, recipient.Id, sendResult.FirstError.Description);
            }
            else
            {
                delivered++;
            }
        }

        version.MarkNotified(delivered);
        await _versionRepository.UpdateNotifiedAsync(version, cancellationToken);

        await _auditLogRepository.CreateAsync(
            AuditLog.CreateSuccess(
                actionType: AuditActionTypes.System,
                action: AuditActions.SystemPolicyNotificationSent,
                userId: request.RequestedBy,
                additionalData:
                    $"{{\"policyVersion\":\"{version.Version}\",\"recipients\":{delivered}}}"),
            cancellationToken);

        _logger.LogInformation(
            "Policy-change notice for version {Version} delivered to {Count} of {Total} active users",
            version.Version, delivered, recipients.Count);

        return new PrivacyPolicyNotifyResultDto { RecipientCount = delivered };
    }
}
