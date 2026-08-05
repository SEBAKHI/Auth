using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;

namespace Auth_API.Modules.UserManagement.EventHandlers;

/// <summary>
/// Tells a person that an administrator permanently destroyed their account.
///
/// <para>
/// Without this the two destruction paths disagreed on transparency: a user who
/// deleted their own account received a final confirmation, while a user
/// deleted BY an administrator was told nothing at all — the account and every
/// trace of it disappeared silently. The published privacy policy commits the
/// controller to being transparent about how personal data is handled, and
/// irreversible destruction is the least defensible moment to go quiet.
/// </para>
///
/// <para>
/// Uses a DISTINCT notification type, not the self-service one: that copy reads
/// "as you requested", which is untrue here and would attribute the action to
/// the person who did not take it.
/// </para>
///
/// <para>
/// The account row is already gone, so the event's snapshot address is its last
/// use and RecipientUserId stays null — there is no user left to resolve
/// language or delivery preferences from. Delivery failures are logged, never
/// propagated: the destruction has already committed and an unsent email cannot
/// undo it.
/// </para>
/// </summary>
public class UserHardDeletedNotificationEventHandler
    : INotificationHandler<UserHardDeletedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<UserHardDeletedNotificationEventHandler> _logger;

    public UserHardDeletedNotificationEventHandler(
        INotificationService notificationService,
        ILogger<UserHardDeletedNotificationEventHandler> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Handle(UserHardDeletedEvent notification, CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.AccountDeletedByAdmin,
                RecipientAddress = notification.Email,
                RecipientUserId = null,
                Variables = new Dictionary<string, object?>()
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send admin-deletion notice for destroyed account {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
