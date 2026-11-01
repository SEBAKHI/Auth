namespace Auth.Domain.Constants;

/// <summary>
/// Reasons the system itself writes to the <c>RevokeReason</c> column of an API key, a webhook
/// key or a machine key (Q6, owner 2026-10-31). Each is stored as a code, never as prose, and
/// the console names it in the reader's language. A reason an administrator typed when revoking
/// a key is free text in the same column, and is shown as typed.
/// </summary>
/// <remarks>
/// The console mirrors <see cref="All"/>, and its test reads this file: a code added here without
/// a name in every language turns that test red.
/// </remarks>
public static class KeyRevocationReasons
{
    /// <summary>The key's application was deleted, and its keys were revoked with it.</summary>
    public const string ApplicationDeleted = "ApplicationDeleted";

    /// <summary>Every code the system writes.</summary>
    public static readonly IReadOnlyList<string> All = [ApplicationDeleted];

    // What deleting an application wrote before it stored a code (f51b6909 until Q6). Those rows
    // stay as they were written and are read as the code.
    private const string LegacyApplicationDeleted = "Application deleted";

    /// <summary>
    /// Splits a stored reason into the system code it holds or, failing that, the administrator's
    /// text. At most one of the two is set.
    /// </summary>
    /// <remarks>
    /// An administrator who typed one of these strings exactly is read as the code too; each of
    /// them says what the code says, so the reader is told what the administrator wrote.
    /// </remarks>
    public static (string? Code, string? Text) Split(string? stored) => stored switch
    {
        ApplicationDeleted or LegacyApplicationDeleted => (ApplicationDeleted, null),
        _ => (null, stored),
    };
}
