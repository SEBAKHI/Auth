using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.EventHandlers;

/// <summary>
/// Emails both the previous and the new owner after an organization ownership
/// transfer completes. Delivery failures are logged, never propagated — the
/// transfer itself has already committed.
/// </summary>
public class OrganizationOwnershipTransferredNotificationEventHandler
    : INotificationHandler<OrganizationOwnershipTransferredEvent>
{
    private readonly IUserRepository _userRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<OrganizationOwnershipTransferredNotificationEventHandler> _logger;

    public OrganizationOwnershipTransferredNotificationEventHandler(
        IUserRepository userRepository,
        INotificationService notificationService,
        ILogger<OrganizationOwnershipTransferredNotificationEventHandler> logger)
    {
        _userRepository = userRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Handle(OrganizationOwnershipTransferredEvent notification, CancellationToken cancellationToken)
    {
        var previousOwner = await _userRepository.GetByIdAsync(notification.PreviousOwnerId, cancellationToken);
        var newOwner = await _userRepository.GetByIdAsync(notification.NewOwnerId, cancellationToken);

        var previousOwnerName = previousOwner?.DisplayName ?? previousOwner?.FirstName ?? "The previous owner";
        var newOwnerName = newOwner?.DisplayName ?? newOwner?.FirstName ?? "The new owner";

        foreach (var recipient in new[] { previousOwner, newOwner })
        {
            if (recipient == null)
            {
                continue;
            }

            var recipientName = recipient.DisplayName ?? recipient.FirstName ?? "User";
            var sendResult = await _notificationService.SendAsync(
                new NotificationRequest
                {
                    TypeCode = NotificationTypeCodes.OwnershipTransferred,
                    RecipientAddress = recipient.Email,
                    RecipientName = recipientName,
                    RecipientUserId = recipient.Id,
                    Variables = new Dictionary<string, object?>
                    {
                        ["UserName"] = recipientName,
                        ["OrganizationName"] = notification.OrganizationName,
                        ["PreviousOwnerName"] = previousOwnerName,
                        ["NewOwnerName"] = newOwnerName
                    }
                },
                cancellationToken);

            if (sendResult.IsError)
            {
                _logger.LogError(
                    "Failed to send ownership-transferred notice for organization {OrganizationId} to user {UserId}: {Error}",
                    notification.OrganizationId, recipient.Id, sendResult.FirstError.Description);
            }
        }
    }
}
