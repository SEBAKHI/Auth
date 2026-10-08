using System.Globalization;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.EventHandlers;

/// <summary>
/// Tells the account owner that two-factor authentication on their account was
/// switched on or off, that its recovery codes or its authenticator app were
/// replaced, or that an administrator removed it.
///
/// The notice is the owner's only signal when the change was not theirs: someone
/// holding the password and a session could otherwise switch the second factor off,
/// or bind an authenticator of their own, in silence (NIST SP 800-63B §4.1.2.1 asks
/// for a notice when a factor is bound, §4.5 when one is removed). It is sent to the
/// address on the account, independently of the session that made the change, and
/// links to the ordinary security page — never to a one-click undo, which a mail
/// scanner's prefetch would trigger.
///
/// Best-effort: the change has already committed. A delivery failure — the
/// notification service's error, or an exception from writing the outbox — is
/// logged and never propagated, because the event's handlers run one after another
/// and an exception here would skip the audit handler and turn the user's completed
/// change into an error response.
/// </summary>
public class TwoFactorChangedNotificationEventHandler :
    INotificationHandler<TwoFactorEnabledEvent>,
    INotificationHandler<TwoFactorDisabledEvent>,
    INotificationHandler<TwoFactorRecoveryCodesRegeneratedEvent>,
    INotificationHandler<TwoFactorAuthenticatorReplacedEvent>,
    INotificationHandler<TwoFactorResetEvent>
{
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TwoFactorChangedNotificationEventHandler> _logger;

    public TwoFactorChangedNotificationEventHandler(
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        TimeProvider timeProvider,
        ILogger<TwoFactorChangedNotificationEventHandler> logger)
    {
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task Handle(TwoFactorEnabledEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.EnabledBy,
            notification.Email,
            notification.DisplayName,
            notification.DeviceName,
            TwoFactorChangeKinds.Enabled,
            cancellationToken);

    public Task Handle(TwoFactorDisabledEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.DisabledBy,
            notification.Email,
            notification.DisplayName,
            notification.DeviceName,
            TwoFactorChangeKinds.Disabled,
            cancellationToken);

    public Task Handle(TwoFactorRecoveryCodesRegeneratedEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.RegeneratedBy,
            notification.Email,
            notification.DisplayName,
            notification.DeviceName,
            TwoFactorChangeKinds.RecoveryCodesRegenerated,
            cancellationToken);

    public Task Handle(TwoFactorAuthenticatorReplacedEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.ReplacedBy,
            notification.Email,
            notification.DisplayName,
            notification.DeviceName,
            TwoFactorChangeKinds.AuthenticatorReplaced,
            cancellationToken);

    // No device: the administrator's browser is not one of the owner's, and the
    // notice must not suggest the owner made the change.
    public Task Handle(TwoFactorResetEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.ResetBy,
            notification.Email,
            notification.DisplayName,
            deviceName: null,
            TwoFactorChangeKinds.ResetByAdministrator,
            cancellationToken);

    private async Task SendAsync(
        Guid userId,
        Guid changedBy,
        string email,
        string displayName,
        string? deviceName,
        string changeKind,
        CancellationToken cancellationToken)
    {
        try
        {
            var sendResult = await _notificationService.SendAsync(
                new NotificationRequest
                {
                    TypeCode = NotificationTypeCodes.TwoFactorChanged,
                    RecipientAddress = email,
                    RecipientName = displayName,
                    RecipientUserId = userId,
                    // The platform's own security notice: its global templates,
                    // whichever application the change was made from.
                    ApplicationId = null,
                    TriggeredBy = changedBy,
                    Variables = new Dictionary<string, object?>
                    {
                        ["UserName"] = displayName,
                        ["ChangeKind"] = changeKind,
                        ["OccurredAtUtc"] = _timeProvider.GetUtcNow().UtcDateTime
                            .ToString("u", CultureInfo.InvariantCulture),
                        ["DeviceName"] = deviceName,
                        ["ManageSecurityLink"] = _emailSettings.BuildFrontendUrl("/profile?tab=security"),
                    }
                },
                cancellationToken);

            if (sendResult.IsError)
            {
                _logger.LogError(
                    "Failed to send the two-factor-changed notice ({ChangeKind}) to user {UserId}: {Error}",
                    changeKind, userId, sendResult.FirstError.Description);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Failed to send the two-factor-changed notice ({ChangeKind}) to user {UserId}",
                changeKind, userId);
        }
    }
}
