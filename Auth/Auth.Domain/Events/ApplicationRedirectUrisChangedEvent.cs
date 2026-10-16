using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an application's redirect allowlists changed (OI-171, folding OI-166 (c)): the
/// public list, the server-side list, or the post-logout list. Each of them decides where this
/// server sends a person's browser, or which callers may redeem a sign-in without the
/// application's key, so a change to any of them is recorded with what the list held before and
/// after, and by whom.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="ApplicationId">The application whose lists changed.</param>
/// <param name="ApplicationCode">Its code (<c>client_id</c>).</param>
/// <param name="RedirectUris">The public list's change; null when it did not change.</param>
/// <param name="ConfidentialRedirectUris">The server-side list's change; null when it did not change.</param>
/// <param name="PostLogoutRedirectUris">The post-logout list's change; null when it did not change.</param>
/// <param name="TriggeredBy">The administrator who saved the application.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record ApplicationRedirectUrisChangedEvent(
    Guid EventId,
    Guid ApplicationId,
    string ApplicationCode,
    RedirectListChange? RedirectUris,
    RedirectListChange? ConfidentialRedirectUris,
    RedirectListChange? PostLogoutRedirectUris,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>True when at least one of the three lists changed.</summary>
    public bool HasChanges =>
        RedirectUris is not null || ConfidentialRedirectUris is not null || PostLogoutRedirectUris is not null;
}

/// <summary>One allowlist before and after a save.</summary>
/// <param name="Before">What the list held before.</param>
/// <param name="After">What it holds now.</param>
public sealed record RedirectListChange(IReadOnlyList<string> Before, IReadOnlyList<string> After)
{
    /// <summary>
    /// The change from <paramref name="before"/> to <paramref name="after"/>, or null when both
    /// hold the same addresses. Compared as sets, byte for byte (ordinal), as the lists are matched:
    /// a reordering is no change, a difference in letter case is one.
    /// </summary>
    public static RedirectListChange? Between(IReadOnlyList<string> before, IReadOnlyList<string> after) =>
        before.ToHashSet(StringComparer.Ordinal).SetEquals(after)
            ? null
            : new RedirectListChange([.. before], [.. after]);
}
