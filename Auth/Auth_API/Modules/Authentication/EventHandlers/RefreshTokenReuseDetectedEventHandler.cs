using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.EventHandlers;

/// <summary>
/// Emails the account owner after a spent refresh token was presented a second
/// time and every session the account held was revoked in response.
///
/// The single action in this message points at the ordinary password-reset
/// page, and that choice is load-bearing rather than incidental: this notice is
/// sent at the one moment when the account may already be under someone else's
/// control, so a link that restored a session would hand it straight to them.
/// For the same reason there is no tokenized one-click action of any kind —
/// mail scanners prefetch links, so such a link would fire before any human
/// read the message. The reasoning matches
/// <see cref="NewDeviceSignInEventHandler"/>, and applies with more force here.
///
/// The copy states what happened and nothing more. It does not claim the
/// account was breached — a refresh token can also be replayed by an ordinary
/// client mishap — and it does not reassure the reader that all is well, since
/// nobody here can know that.
///
/// Delivery failures are logged, never propagated: the revocation has already
/// committed and an unsent email cannot undo it.
/// </summary>
public class RefreshTokenReuseDetectedEventHandler
    : INotificationHandler<RefreshTokenReuseDetectedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<RefreshTokenReuseDetectedEventHandler> _logger;

    public RefreshTokenReuseDetectedEventHandler(
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<RefreshTokenReuseDetectedEventHandler> logger)
    {
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(
        RefreshTokenReuseDetectedEvent notification,
        CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.SessionsRevokedTokenReuse,
                RecipientAddress = notification.Email,
                RecipientName = notification.DisplayName,
                RecipientUserId = notification.UserId,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = notification.DisplayName,
                    // An em dash rather than an empty string or a word: the
                    // template is per-language, so an English "Unknown" would
                    // land inside an Arabic email. A dash reads the same in all
                    // seven.
                    ["IpAddress"] = notification.IpAddress ?? "—",
                    ["DetectedAt"] = notification.DetectedAtUtc.ToString("u"),
                    ["SecureAccountLink"] = _emailSettings.BuildFrontendUrl("/forgot-password")
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send session-revocation notice to user {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
