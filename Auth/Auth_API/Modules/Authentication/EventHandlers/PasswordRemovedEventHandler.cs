using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.EventHandlers;

/// <summary>
/// Tells the account owner that the account's password was removed (OI-215), and why:
/// they removed it, or a sign-in by code proved their address for the first time and
/// the password set before that proof was retired.
/// </summary>
/// <remarks>
/// The quietest change an account can undergo is turning code-only: the owner can still
/// sign in by code and would notice nothing. This message breaks that silence. It
/// carries no secret, and its link is the ordinary security page, never a one-click
/// undo a mail scanner's prefetch could fire. Delivery failure is logged and never
/// propagated: the change has committed. Best-effort, like every notice of this kind.
/// </remarks>
public class PasswordRemovedEventHandler : INotificationHandler<PasswordRemovedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<PasswordRemovedEventHandler> _logger;

    public PasswordRemovedEventHandler(
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<PasswordRemovedEventHandler> logger)
    {
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task Handle(PasswordRemovedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var sendResult = await _notificationService.SendAsync(
                new NotificationRequest
                {
                    TypeCode = NotificationTypeCodes.PasswordRemoved,
                    RecipientAddress = notification.Email,
                    RecipientName = notification.DisplayName,
                    RecipientUserId = notification.UserId,
                    // The platform's own security notice: its global templates.
                    ApplicationId = null,
                    TriggeredBy = notification.RemovedBy,
                    Variables = new Dictionary<string, object?>
                    {
                        ["UserName"] = notification.DisplayName,
                        ["Reason"] = notification.Reason,
                        ["RemovedAt"] = DateTime.UtcNow.ToString("u"),
                        ["ManageSecurityLink"] = _emailSettings.BuildFrontendUrl("/profile?tab=security"),
                    }
                },
                cancellationToken);

            if (sendResult.IsError)
            {
                _logger.LogError(
                    "Failed to send the password-removed notice to user {UserId}: {Error}",
                    notification.UserId, sendResult.FirstError.Description);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to send the password-removed notice to user {UserId}", notification.UserId);
        }
    }
}
