using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.EventHandlers;

/// <summary>
/// Emails the account owner when a sign-in comes from a device they have not
/// been seen on before.
///
/// The message carries one action, and it is an ordinary unauthenticated page:
/// "if this wasn't you, change your password". There is deliberately no
/// one-click "this wasn't me" or "this was me" link. A tokenized action link in
/// email is phishable on its own, and — decisively — mail scanners prefetch
/// links, so a link that terminated sessions would fire before any human read
/// it and lock the legitimate user out of their own account.
///
/// Delivery failures are logged, never propagated: the sign-in has already
/// completed and returned.
/// </summary>
public class NewDeviceSignInEventHandler : INotificationHandler<NewDeviceSignInEvent>
{
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<NewDeviceSignInEventHandler> _logger;

    public NewDeviceSignInEventHandler(
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<NewDeviceSignInEventHandler> logger)
    {
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(NewDeviceSignInEvent notification, CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.NewDeviceSignIn,
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
                    ["DeviceName"] = notification.DeviceName ?? "—",
                    ["IpAddress"] = notification.IpAddress ?? "—",
                    ["SignedInAt"] = notification.SignedInAtUtc.ToString("u"),
                    ["SecureAccountLink"] = _emailSettings.BuildFrontendUrl("/forgot-password")
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send new-device sign-in notice to user {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
