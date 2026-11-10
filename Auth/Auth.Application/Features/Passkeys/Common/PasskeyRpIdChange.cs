using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Features.Passkeys.Common;

/// <inheritdoc />
/// <remarks>
/// The RP ID after the change and the one configured now are read as the PK2 section rule reads
/// them (<see cref="SystemSettingsValueValidator.PasskeyRpIds"/>), so the two rules agree on
/// what a change of it is. The question asked of the rows is the posture's own
/// (<see cref="IUserPasskeyRepository.HasLiveOfAnotherRpIdAsync"/>): whether any live passkey,
/// of any account, is bound to an RP ID other than the one the change leaves. Without the
/// passkey tables it is never true, so a deployment without the schema saves as before.
/// </remarks>
public sealed class PasskeyRpIdChange : IPasskeyRpIdChange
{
    private readonly IUserPasskeyRepository _passkeyRepository;

    public PasskeyRpIdChange(IUserPasskeyRepository passkeyRepository)
    {
        _passkeyRepository = passkeyRepository;
    }

    /// <inheritdoc />
    public async Task<Error?> RefusalAsync(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> payload,
        Func<string, string?> afterChange,
        Func<string, string?> now,
        CancellationToken cancellationToken)
    {
        if (section.Key != PasskeySettings.SectionName)
        {
            return null;
        }

        var (after, current) = SystemSettingsValueValidator.PasskeyRpIds(payload, section, afterChange, now);
        if (after == current
            || !await _passkeyRepository.HasLiveOfAnotherRpIdAsync(after, cancellationToken))
        {
            return null;
        }

        return SystemSettingsErrors.InvalidFieldValue(nameof(PasskeySettings.RpId), PasskeyConfiguration.OtherRpIdLive);
    }
}
