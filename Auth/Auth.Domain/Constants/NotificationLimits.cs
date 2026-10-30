namespace Auth.Domain.Constants;

/// <summary>
/// Bounds every notification render shares.
/// </summary>
public static class NotificationLimits
{
    /// <summary>
    /// Largest output of one notification render, in UTF-8 bytes counted after encoding
    /// (100 KB, decided by the owner on 2026-10-29). Gmail clips a message past about 102 KB, so
    /// the layout render, whose output is the final email HTML, stays whole. The subject, the
    /// body, the text part, SMS and push renders share the one number: they are far smaller, and
    /// one rule is simpler than five. The sources may be longer (512,000 characters): those
    /// bound the request, this bounds what a render builds.
    /// </summary>
    public const int MaxRenderedBytes = 100_000;
}
