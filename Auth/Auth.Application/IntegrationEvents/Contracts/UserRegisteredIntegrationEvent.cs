namespace Auth.Application.IntegrationEvents.Contracts;

/// <summary>
/// Published when a new user registers in the Auth system.
/// Consumers: Email service (welcome email), CRM, analytics.
/// </summary>
public record UserRegisteredIntegrationEvent(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    DateTime RegisteredAt) : IntegrationEvent
{
    public override string EventType => "auth.user.registered";
}
