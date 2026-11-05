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
/// Tells the account owner that their password was replaced.
///
/// Until this handler existed the system changed passwords in complete silence, which
/// makes a stolen session or a stolen mailbox worth more than it should be: the quietest
/// way to take an account is to change its password and have nobody told.
///
/// One message covers both routes - the profile screen and the reset link - because the
/// aggregate raises one event for both and cannot tell them apart. The copy says so rather
/// than guessing. Delivery failure is logged and never propagated: the password is already
/// changed, and failing the request afterwards would only confuse the caller.
/// </summary>
public class PasswordChangedEventHandler : INotificationHandler<PasswordChangedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<PasswordChangedEventHandler> _logger;

    public PasswordChangedEventHandler(
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<PasswordChangedEventHandler> logger)
    {
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(PasswordChangedEvent notification, CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordChanged,
                RecipientAddress = notification.Email,
                RecipientName = notification.DisplayName,
                RecipientUserId = notification.UserId,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = notification.DisplayName,
                    ["ChangedAt"] = DateTime.UtcNow.ToString("u"),
                    ["ManageSecurityLink"] =
                        _emailSettings.BuildFrontendUrl("/profile?tab=security"),
                    // S20 (P7-R12): the passkeys that stay after the change, so the owner can
                    // spot one they do not recognise. The template encodes each name.
                    ["PasskeyCount"] = notification.Passkeys.Count,
                    ["Passkeys"] = notification.Passkeys
                        .Select(passkey => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                        {
                            ["Name"] = passkey.Name,
                            ["CreatedAt"] = passkey.CreatedAt.ToString("u", CultureInfo.InvariantCulture),
                            ["LastUsedAt"] = passkey.LastUsedAt?.ToString("u", CultureInfo.InvariantCulture),
                        })
                        .ToList(),
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send password-changed notice to user {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
