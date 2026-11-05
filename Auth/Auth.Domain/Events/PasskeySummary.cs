namespace Auth.Domain.Events;

/// <summary>
/// One live passkey as a notice lists it (P7-R12): its name and when it was added and last
/// used, so the owner can spot one they do not recognise. Nothing a device or a server
/// could sign with.
/// </summary>
/// <param name="Name">The name its owner gave it.</param>
/// <param name="CreatedAt">When it was added (UTC).</param>
/// <param name="LastUsedAt">When it last signed in (UTC), or null.</param>
public sealed record PasskeySummary(string Name, DateTime CreatedAt, DateTime? LastUsedAt);
