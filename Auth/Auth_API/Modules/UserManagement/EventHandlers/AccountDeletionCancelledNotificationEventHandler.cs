using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.UserManagement.EventHandlers;

/// <summary>
/// Emails the recovery confirmation after a pending deletion is cancelled —
/// a security signal too: if the owner did not recover the account, their
/// credentials are compromised. Delivery failures are logged, never
/// propagated — the recovery itself has already committed.
/// </summary>
public class AccountDeletionCancelledNotificationEventHandler
    : INotificationHandler<AccountDeletionCancelledEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<AccountDeletionCancelledNotificationEventHandler> _logger;

    public AccountDeletionCancelledNotificationEventHandler(
        INotificationService notificationService,
        ILogger<AccountDeletionCancelledNotificationEventHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Handle(AccountDeletionCancelledEvent notification, CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.AccountDeletionCancelled,
                RecipientAddress = notification.Email,
                RecipientName = notification.DisplayName,
                RecipientUserId = notification.UserId,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = notification.DisplayName,
                    ["CancelledAt"] = notification.CancelledAtUtc.ToString("u")
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send deletion-cancelled notice to user {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
