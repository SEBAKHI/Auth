using System.Text.Json;
using Auth.Application.SystemSettings;

namespace Auth.Application.Interfaces;

/// <summary>
/// Keeps a console change of the browser-session idle limit from bringing idle
/// sessions back (OI-154, owner decision D-74-2 (a)). A session dies idle only
/// lazily, when a read finds it, so raising the limit or switching it off would
/// make every browser session nobody read since live again. The settings writes
/// (save and reset) call this before they change a section, so the sessions idle
/// under the limit in force are ended first.
/// </summary>
public interface IIdpSessionIdleLimitChange
{
    /// <summary>
    /// Ends, in one statement, the browser sessions already idle under the limit
    /// in force, when the change raises the limit or switches it off. Does nothing
    /// for another section, a lower or equal limit, or a limit that was already
    /// off. A failure propagates, so the change is not saved.
    /// </summary>
    /// <param name="section">The section being changed.</param>
    /// <param name="payload">The fields the change sets (empty for a reset).</param>
    /// <param name="baseline">What a field reads once the change is saved when the payload omits it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EndSessionsIdleBeforeChangeAsync(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> payload,
        Func<string, string?> baseline,
        CancellationToken cancellationToken);
}
