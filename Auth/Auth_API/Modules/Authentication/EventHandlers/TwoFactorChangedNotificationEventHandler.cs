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
/// replaced, that the emailed code became or stopped being its second factor (S24),
/// that an administrator removed it, or that a passkey was added or stopped working
/// (S20: by its owner, or revoked by a password change, a password reset or a lock).
///
/// One change, one notice: when one factor takes the place of the other, two events
/// are raised (the old factor off, the new one on) and only the new factor's sends —
/// it is the one the owner needs to recognise, and its kind names the replacement:
/// the emailed code's wording says an app linked before no longer works, and an app
/// in the emailed code's place has a kind of its own.
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
    INotificationHandler<TwoFactorResetEvent>,
    INotificationHandler<TwoFactorEmailCodeEnabledEvent>,
    INotificationHandler<TwoFactorEmailCodeDisabledEvent>,
    INotificationHandler<PasskeyRegisteredEvent>,
    INotificationHandler<PasskeyRemovedEvent>
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

    // An app in place of the emailed code names that change: a plain "switched on"
    // would not tell the owner the codes by email stopped working.
    public Task Handle(TwoFactorEnabledEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.EnabledBy,
            notification.Email,
            notification.DisplayName,
            notification.DeviceName,
            notification.ReplacedEmailCode
                ? TwoFactorChangeKinds.AuthenticatorReplacedEmailCode
                : TwoFactorChangeKinds.Enabled,
            cancellationToken);

    // The app switched off because the emailed code took its place: that event's
    // notice, raised just after, is the one the owner receives.
    public Task Handle(TwoFactorDisabledEvent notification, CancellationToken cancellationToken) =>
        notification.ReplacedByEmailCode
            ? Task.CompletedTask
            : SendAsync(
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
    // notice must not suggest the owner made the change. How many passkeys went with the
    // factor (REVIEW-FIXES 150 F16): the wording says they went too only above 0, and none
    // gets a notice of its own.
    public Task Handle(TwoFactorResetEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.ResetBy,
            notification.Email,
            notification.DisplayName,
            deviceName: null,
            TwoFactorChangeKinds.ResetByAdministrator,
            cancellationToken,
            new Dictionary<string, object?> { ["PasskeysRemoved"] = notification.PasskeysRemoved });

    public Task Handle(TwoFactorEmailCodeEnabledEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.EnabledBy,
            notification.Email,
            notification.DisplayName,
            notification.DeviceName,
            TwoFactorChangeKinds.EmailCodeEnabled,
            cancellationToken);

    // The emailed code switched off because an authenticator app took its place: the
    // app's "enabled" notice, raised just after, is the one the owner receives.
    public Task Handle(TwoFactorEmailCodeDisabledEvent notification, CancellationToken cancellationToken) =>
        notification.Reason == TwoFactorEmailCodeDisableReasons.ReplacedByAuthenticator
            ? Task.CompletedTask
            : SendAsync(
                notification.UserId,
                notification.DisabledBy,
                notification.Email,
                notification.DisplayName,
                notification.DeviceName,
                TwoFactorChangeKinds.EmailCodeDisabled,
                cancellationToken);

    // S20 (FA11): a passkey added to the account is a new way in, so its owner is told,
    // independently of the session that added it.
    public Task Handle(PasskeyRegisteredEvent notification, CancellationToken cancellationToken) =>
        SendAsync(
            notification.UserId,
            notification.RegisteredBy,
            notification.Email,
            notification.DisplayName,
            notification.DeviceName,
            TwoFactorChangeKinds.PasskeyRegistered,
            cancellationToken);

    // S20 (REVIEW 150 F12): every passkey that stopped working without its owner's own word is
    // told to them, each in words of its reason — which passkey, why, and what to do — and the
    // owner's own removal keeps its words. An administrator's two-factor reset has a notice of
    // its own and sends none per passkey; the scripts the owner runs raise no event.
    public Task Handle(PasskeyRemovedEvent notification, CancellationToken cancellationToken) =>
        !NoticeKindByRemovalReason.TryGetValue(notification.Reason, out var kind)
            ? Task.CompletedTask
            : SendAsync(
                notification.UserId,
                notification.RemovedBy ?? notification.UserId,
                notification.Email,
                notification.DisplayName,
                notification.DeviceName,
                kind,
                cancellationToken,
                new Dictionary<string, object?>
                {
                    // The template encodes the name: it is the owner's own text, or an intruder's.
                    ["PasskeyName"] = notification.Name,
                    ["PasskeyAddedAt"] = notification.AddedAtUtc.ToString("u", CultureInfo.InvariantCulture),
                });

    /// <summary>
    /// The removals told to the owner per passkey, with the wording each gets (REVIEW 150 F12).
    /// A reason that is not here sends nothing.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NoticeKindByRemovalReason =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PasskeyRemovalReasons.User] = TwoFactorChangeKinds.PasskeyRemoved,
            [PasskeyRemovalReasons.PasswordChange] = TwoFactorChangeKinds.PasskeyRemovedByPasswordChange,
            [PasskeyRemovalReasons.PasswordReset] = TwoFactorChangeKinds.PasskeyRemovedByPasswordReset,
            [PasskeyRemovalReasons.AdminLock] = TwoFactorChangeKinds.PasskeyRemovedByAdminLock,
        };

    private async Task SendAsync(
        Guid userId,
        Guid changedBy,
        string email,
        string displayName,
        string? deviceName,
        string changeKind,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? passkeyDetails = null)
    {
        try
        {
            var variables = new Dictionary<string, object?>
            {
                ["UserName"] = displayName,
                ["ChangeKind"] = changeKind,
                ["OccurredAtUtc"] = _timeProvider.GetUtcNow().UtcDateTime
                    .ToString("u", CultureInfo.InvariantCulture),
                ["DeviceName"] = deviceName,
                ["ManageSecurityLink"] = _emailSettings.BuildFrontendUrl("/profile?tab=security"),
            };
            foreach (var (name, value) in passkeyDetails ?? new Dictionary<string, object?>())
            {
                variables[name] = value;
            }

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
                    Variables = variables
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
