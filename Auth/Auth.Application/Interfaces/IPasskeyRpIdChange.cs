using System.Text.Json;
using Auth.Application.SystemSettings;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Keeps a console change of <c>Passkeys:RpId</c> from leaving live passkeys bound to an RP ID
/// that no longer runs (S20 PR C, PK3, T-C9). A passkey signs in only under the RP ID it was
/// made under, so a new value — the empty one included — would make every live passkey of the
/// old one sign in nowhere and count as no factor. The settings writes (save and reset) ask
/// this before they change anything; moving the RP ID on purpose is the retirement the setup
/// tool prints ([S20-O10]), then the change.
/// </summary>
public interface IPasskeyRpIdChange
{
    /// <summary>
    /// The refusal of a change that moves the RP ID while a live passkey is bound to another
    /// one than the change leaves; null for another section, a change that leaves the RP ID as
    /// it is, or no such passkey. Reads, and writes nothing.
    /// </summary>
    /// <param name="section">The section being changed.</param>
    /// <param name="payload">The fields the change sets (empty for a reset).</param>
    /// <param name="afterChange">What a field reads once the change is made when the payload omits it.</param>
    /// <param name="now">The running configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Error?> RefusalAsync(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> payload,
        Func<string, string?> afterChange,
        Func<string, string?> now,
        CancellationToken cancellationToken);
}
