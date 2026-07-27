using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.UserManagement.EventHandlers;

/// <summary>
/// Emails the deletion acknowledgment after a deletion request: the grace
/// deadline and how to recover the account. Delivery failures are logged,
/// never propagated — the deletion request itself has already committed.
/// </summary>
public class AccountDeletionRequestedNotificationEventHandler
    : INotificationHandler<AccountDeletionRequestedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly AccountDeletionSettings _accountDeletionSettings;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<AccountDeletionRequestedNotificationEventHandler> _logger;

    public AccountDeletionRequestedNotificationEventHandler(
        INotificationService notificationService,
        IOptions<AccountDeletionSettings> accountDeletionSettings,
        IOptions<EmailSettings> emailSettings,
        ILogger<AccountDeletionRequestedNotificationEventHandler> logger)
    {
        _notificationService = notificationService;
        _accountDeletionSettings = accountDeletionSettings.Value;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(AccountDeletionRequestedEvent notification, CancellationToken cancellationToken)
    {
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.AccountDeletionRequested,
                RecipientAddress = notification.Email,
                RecipientName = notification.DisplayName,
                RecipientUserId = notification.UserId,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = notification.DisplayName,
                    ["GraceEndsAt"] = notification.GraceEndsAtUtc.ToString("u"),
                    ["GraceDays"] = _accountDeletionSettings.GraceDays,
                    ["RecoveryLink"] = _emailSettings.BuildFrontendUrl("/account-recovery")
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send deletion-requested notice to user {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
