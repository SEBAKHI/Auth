using System.Text.Json;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Events;
using Auth.Domain.Constants;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Creates an audit log entry when a user logs in.
/// </summary>
/// <remarks>
/// <para>
/// The row's details say what the sign-in proved (S20, R-EVT): <c>methods</c>, the stored flags
/// of <see cref="Auth.Domain.ValueObjects.AuthenticationMethods"/> (null when the exit named none),
/// and <c>amr</c>, the same values an access token would carry. Built with System.Text.Json
/// (ARR-1058), never by interpolation.
/// </para>
/// <para>
/// Best-effort: a failure to write the row is logged, never propagated. The session is already
/// minted when this runs, and an exception here would turn the completed sign-in into an error
/// response whose tokens never reach the person.
/// </para>
/// </remarks>
public class UserLoggedInAuditEventHandler : INotificationHandler<UserLoggedInEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<UserLoggedInAuditEventHandler> _logger;

    public UserLoggedInAuditEventHandler(
        IAuditLogRepository auditLogRepository,
        ILogger<UserLoggedInAuditEventHandler> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var log = Auth.Domain.Entities.AuditLog.CreateSuccess(
                actionType: AuditActionTypes.Authentication,
                action: AuditActions.UserLogin,
                userId: notification.UserId,
                performedBy: notification.UserId,
                entityType: "User",
                entityId: notification.UserId,
                ipAddress: notification.IpAddress,
                userAgent: notification.UserAgent,
                additionalData: JsonSerializer.Serialize(new
                {
                    methods = notification.Methods.ToStored(),
                    amr = notification.Methods.ToAmrValues(),
                }));

            await _auditLogRepository.CreateAsync(log, cancellationToken);
            _logger.LogDebug("Audit log created for UserLoggedInEvent: {UserId}", notification.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to write the audit row for UserLoggedInEvent of user {UserId}",
                notification.UserId);
        }
    }
}
