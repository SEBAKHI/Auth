using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.EventHandlers;

/// <summary>
/// Tells the account owner that their account, which had no password, now has one.
///
/// This is a different message from a rotation and needs to exist separately. An
/// external-only account could previously be opened only by whoever held the Google or
/// Apple account; once a password exists it can be opened by anyone who knows that
/// password, and the owner is the only person who can say whether they chose it.
///
/// Delivery failure is logged and never propagated: the password is already set, and
/// failing the request would leave the caller unable to tell what happened.
/// </summary>
public class PasswordCreatedEventHandler : INotificationHandler<PasswordCreatedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<PasswordCreatedEventHandler> _logger;

    public PasswordCreatedEventHandler(
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<PasswordCreatedEventHandler> logger)
    {
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(PasswordCreatedEvent notification, CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordCreated,
                RecipientAddress = notification.Email,
                RecipientName = notification.DisplayName,
                RecipientUserId = notification.UserId,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = notification.DisplayName,
                    ["SetAt"] = DateTime.UtcNow.ToString("u"),
                    // The ordinary security page, never a tokenized one-click undo: mail
                    // scanners prefetch links, so anything that revoked a credential would
                    // fire before a human read the message.
                    ["ManageSecurityLink"] =
                        _emailSettings.BuildFrontendUrl("/profile?tab=security"),
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send password-created notice to user {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
