namespace Auth.Domain.Enums;

/// <summary>
/// Delivery channel for a notification. Stored as TINYINT in the database
/// (CK constraints on NotificationTemplates/NotificationLayouts must match).
/// Adding a channel requires a compiled INotificationChannel strategy, so this
/// is a code enum rather than a lookup table.
/// </summary>
public enum NotificationChannelType : byte
{
    Email = 1,
    Sms = 2,
    Push = 3
}
