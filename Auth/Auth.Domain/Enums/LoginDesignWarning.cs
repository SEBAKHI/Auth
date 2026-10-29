namespace Auth.Domain.Enums;

/// <summary>
/// Markup a sign-in page design may hold that will never run there. A design is shown in a
/// frame whose policy runs no script but the frame's own, so these are reported to the author
/// when the design is saved or previewed, and never refused: the design is the administrator's.
/// </summary>
public enum LoginDesignWarning
{
    /// <summary>A <c>&lt;script&gt;</c> element or a <c>javascript:</c> address.</summary>
    Script = 1,

    /// <summary>An event handler attribute (<c>onclick</c>, <c>onload</c>, …).</summary>
    EventHandler = 2
}
