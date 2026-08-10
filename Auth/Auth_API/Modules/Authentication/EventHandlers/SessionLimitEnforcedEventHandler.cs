using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.EventHandlers;

/// <summary>
/// Tells the account owner that signing in somewhere new signed them out
/// somewhere else, because the account is at its concurrent session limit.
///
/// This mail is not optional politeness. A device that silently drops out of a
/// signed-in account is indistinguishable from a hijacking, and a user who
/// cannot explain it will either panic or — worse — learn to ignore it.
///
/// One message per enforcement, listing every session that ended. An operator
/// lowering the limit from twenty to five evicts fifteen sessions on the next
/// sign-in; fifteen separate emails in the same second is how security mail
/// becomes noise. Like every other notification on the sign-in path, delivery
/// failure is logged and never propagated: the user is already signed in.
/// </summary>
public class SessionLimitEnforcedEventHandler : INotificationHandler<SessionLimitEnforcedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<SessionLimitEnforcedEventHandler> _logger;

    public SessionLimitEnforcedEventHandler(
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<SessionLimitEnforcedEventHandler> logger)
    {
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(SessionLimitEnforcedEvent notification, CancellationToken cancellationToken)
    {
        // Named devices only, and the em dash for the rest: the template is
        // per-language, so an English "Unknown" would land inside an Arabic
        // email. A dash reads the same in all seven.
        var endedDevices = string.Join(
            ", ",
            notification.EndedSessions.Select(s => s.DeviceName ?? "—"));

        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.SessionLimitEnforced,
                RecipientAddress = notification.Email,
                RecipientName = notification.DisplayName,
                RecipientUserId = notification.UserId,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = notification.DisplayName,
                    ["EndedCount"] = notification.EndedSessions.Count,
                    ["EndedDevices"] = endedDevices,
                    ["NewDeviceName"] = notification.NewDeviceName ?? "—",
                    ["SessionLimit"] = notification.Limit,
                    ["SignedOutAt"] = notification.OccurredAtUtc.ToString("u"),
                    // Same destination as the new-device notice, and for the same
                    // reason: a tokenized one-click action in email is phishable,
                    // and mail scanners prefetch links, so a link that terminated
                    // sessions would fire before a human ever read it.
                    ["ManageSessionsLink"] = _emailSettings.BuildFrontendUrl("/profile")
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send session-limit notice to user {UserId}: {Error}",
                notification.UserId, sendResult.FirstError.Description);
        }
    }
}
