using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an application's launch addresses changed (OI-192): its initiate-login URI, or its
/// base URL. Since an accepted invitation sends the invitee's browser to the first, or to the
/// second when there is no first, both are destinations this server chooses for a person, like
/// the redirect allowlists (<see cref="ApplicationRedirectUrisChangedEvent"/>), so a change to
/// either is recorded with what it held before and after, and by whom.
/// </summary>
/// <param name="EventId">Generated once, when the event is created.</param>
/// <param name="ApplicationId">The application whose addresses changed.</param>
/// <param name="ApplicationCode">Its code (<c>client_id</c>).</param>
/// <param name="InitiateLoginUri">The initiate-login URI's change; null when it did not change.</param>
/// <param name="BaseUrl">The base URL's change; null when it did not change.</param>
/// <param name="TriggeredBy">The administrator who saved the application.</param>
/// <param name="OccurredAt">When the change was stored.</param>
public sealed record ApplicationLaunchAddressesChangedEvent(
    Guid EventId,
    Guid ApplicationId,
    string ApplicationCode,
    LaunchAddressChange? InitiateLoginUri,
    LaunchAddressChange? BaseUrl,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>True when at least one of the two addresses changed.</summary>
    public bool HasChanges => InitiateLoginUri is not null || BaseUrl is not null;
}

/// <summary>One address before and after a save.</summary>
/// <param name="Before">What it held before; null for none.</param>
/// <param name="After">What it holds now; null for none.</param>
public sealed record LaunchAddressChange(string? Before, string? After)
{
    /// <summary>
    /// The change from <paramref name="before"/> to <paramref name="after"/>, or null when they are
    /// the same address. Compared byte for byte (ordinal), as a browser would be sent there: a
    /// difference in letter case is a change.
    /// </summary>
    public static LaunchAddressChange? Between(string? before, string? after) =>
        string.Equals(before, after, StringComparison.Ordinal) ? null : new LaunchAddressChange(before, after);
}
