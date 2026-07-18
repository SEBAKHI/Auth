namespace Auth.Domain.Enums;

/// <summary>
/// Lifecycle of an outbox message. Stored as TINYINT in NotificationOutbox
/// (the CK constraint must match).
/// </summary>
public enum NotificationDeliveryStatus : byte
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Retry = 3,
    Dead = 4
}
