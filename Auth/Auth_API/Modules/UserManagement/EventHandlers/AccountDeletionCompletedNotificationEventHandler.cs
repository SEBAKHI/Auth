using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.UserManagement.EventHandlers;

/// <summary>
/// Emails the final destruction confirmation. The account no longer exists:
/// the snapshot address and name carried by the event are their last use, and
/// RecipientUserId stays null — there is no user row left to resolve language
/// or delivery preferences from. Delivery failures are logged, never
/// propagated — the destruction has already committed.
/// </summary>
public class AccountDeletionCompletedNotificationEventHandler
    : INotificationHandler<AccountDeletionCompletedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<AccountDeletionCompletedNotificationEventHandler> _logger;

    public AccountDeletionCompletedNotificationEventHandler(
        INotificationService notificationService,
        ILogger<AccountDeletionCompletedNotificationEventHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Handle(AccountDeletionCompletedEvent notification, CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.AccountDeletionCompleted,
                RecipientAddress = notification.Email,
                RecipientName = notification.DisplayName,
                RecipientUserId = null,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = notification.DisplayName
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send deletion-completed notice for destroyed account {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
