using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <inheritdoc />
/// <remarks>
/// The limit in force is the request's settings snapshot, bound before the
/// settings write reloads the configuration. The limit after the change is the
/// payload's value, or the baseline's when the payload omits it (a save replaces
/// the section's whole override row), read through the same normalization the
/// sessions are judged by.
/// </remarks>
public sealed class IdpSessionIdleLimitChange : IIdpSessionIdleLimitChange
{
    private const string IdleMinutesField = nameof(IdentityProviderSettings.IdpBrowserSessionIdleMinutes);

    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IOptionsSnapshot<IdentityProviderSettings> _idpSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdpSessionIdleLimitChange> _logger;

    public IdpSessionIdleLimitChange(
        IIdpSessionRepository idpSessionRepository,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        TimeProvider timeProvider,
        ILogger<IdpSessionIdleLimitChange> logger)
    {
        _idpSessionRepository = idpSessionRepository;
        _idpSettings = idpSettings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EndSessionsIdleBeforeChangeAsync(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> payload,
        Func<string, string?> baseline,
        CancellationToken cancellationToken)
    {
        if (!section.ConfigRoot.Equals(IdentityProviderSettings.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var inForce = _idpSettings.Value.IdpBrowserSessionIdleLimit;
        var afterChange = IdentityProviderSettings.BrowserSessionIdleLimitFor(MinutesAfterChange(section, payload, baseline));

        // A lower limit ends more sessions at their next read, and a limit that
        // was off left no session idle: neither brings anything back.
        if (inForce <= TimeSpan.Zero || (afterChange > TimeSpan.Zero && afterChange <= inForce))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var ended = await _idpSessionRepository.EndIdleBrowserSessionsAsync(now, now - inForce, cancellationToken);

        _logger.LogInformation(
            "Ended {Count} IdP browser sessions idle for {IdleMinutes} minutes before the idle limit changed to {NewIdleMinutes}",
            ended, (int)inForce.TotalMinutes, (int)afterChange.TotalMinutes);
    }

    private static int MinutesAfterChange(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> payload,
        Func<string, string?> baseline)
    {
        foreach (var (path, value) in payload)
        {
            if (path.Equals(IdleMinutesField, StringComparison.OrdinalIgnoreCase))
            {
                return ParseOrDefault(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText());
            }
        }

        // A key absent from every layer runs with the class default.
        return ParseOrDefault(baseline(section.FullKey(IdleMinutesField)));
    }

    private static int ParseOrDefault(string? text) =>
        int.TryParse(text, out var minutes) ? minutes : IdentityProviderSettings.DefaultIdpBrowserSessionIdleMinutes;
}
