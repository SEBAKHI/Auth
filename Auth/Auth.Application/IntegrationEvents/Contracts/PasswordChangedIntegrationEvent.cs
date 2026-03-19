namespace Auth.Application.IntegrationEvents.Contracts;

/// <summary>
/// Published when a user's password is changed.
/// Consumers: Notification service (security alert email).
/// </summary>
public record PasswordChangedIntegrationEvent(
    Guid UserId,
    Guid ChangedBy,
    DateTime ChangedAt) : IntegrationEvent
{
    public override string EventType => "auth.password.changed";
}
