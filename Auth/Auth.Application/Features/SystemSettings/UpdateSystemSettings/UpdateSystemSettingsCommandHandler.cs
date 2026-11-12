using System.Text.Json;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.SystemSettings.UpdateSystemSettings;

/// <summary>
/// Handler for saving a section's overrides. Every path is whitelisted
/// against the settings registry (unknown, read-only, and secret-owned
/// fields are rejected), values are validated with rules that mirror the
/// startup fail-fasts, and the in-process configuration layer is reloaded on
/// success so hot consumers rebind immediately.
/// </summary>
public class UpdateSystemSettingsCommandHandler : IRequestHandler<UpdateSystemSettingsCommand, ErrorOr<SystemSettingsSectionDto>>
{
    private readonly ISystemSettingsRepository _systemSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly IStartupValuesSnapshot _startupSnapshot;
    private readonly ISystemSettingsReloader _reloader;
    private readonly IPublisher _publisher;
    private readonly IIdpSessionIdleLimitChange _idleLimitChange;
    private readonly IPasskeyRpIdChange _passkeyRpIdChange;
    private readonly ILogger<UpdateSystemSettingsCommandHandler> _logger;

    // Resolved from the container in every host (registered by Program.cs); a test that builds the
    // handler without one gets the rules of an environment that is not Production.
    private readonly IEnvironmentInfo? _environment;

    public UpdateSystemSettingsCommandHandler(
        ISystemSettingsRepository systemSettingsRepository,
        IUserRepository userRepository,
        IConfiguration configuration,
        IStartupValuesSnapshot startupSnapshot,
        ISystemSettingsReloader reloader,
        IPublisher publisher,
        IIdpSessionIdleLimitChange idleLimitChange,
        IPasskeyRpIdChange passkeyRpIdChange,
        ILogger<UpdateSystemSettingsCommandHandler> logger,
        IEnvironmentInfo? environment = null)
    {
        _systemSettingsRepository = systemSettingsRepository;
        _userRepository = userRepository;
        _configuration = configuration;
        _startupSnapshot = startupSnapshot;
        _reloader = reloader;
        _publisher = publisher;
        _idleLimitChange = idleLimitChange;
        _passkeyRpIdChange = passkeyRpIdChange;
        _logger = logger;
        _environment = environment;
    }

    public async Task<ErrorOr<SystemSettingsSectionDto>> Handle(UpdateSystemSettingsCommand request, CancellationToken cancellationToken)
    {
        var section = SystemSettingsRegistry.TryGet(request.SectionKey);
        if (section is null)
        {
            return SystemSettingsErrors.SectionNotFound(request.SectionKey);
        }

        if (!section.Editable)
        {
            return SystemSettingsErrors.SectionReadOnly(section.Key);
        }

        if (request.Overrides.ValueKind != JsonValueKind.Object)
        {
            return SystemSettingsErrors.InvalidFieldValue(section.Key, "the override payload must be a JSON object.");
        }

        var flattened = JsonOverrideFlattener.Flatten(request.Overrides, expandArrays: false);

        // Whitelist + value validation, collecting every problem so the
        // admin fixes the form in one round trip.
        var errors = new List<Error>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, value) in flattened)
        {
            // The rules below read a field's first value, and the stored row's reader keeps its
            // last: with two keys naming one field, the value judged is not the value used.
            if (!named.Add(path))
            {
                errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "the payload names this field more than once."));
                continue;
            }

            var field = SystemSettingsRegistry.TryGetField(section, path);
            if (field is null)
            {
                errors.Add(SystemSettingsErrors.UnknownField(path));
                continue;
            }

            if (field.Sensitive || SecretOwnedKeys.IsSecretOwned(section.FullKey(field)))
            {
                errors.Add(SystemSettingsErrors.SecretManagedField(path));
                continue;
            }

            if (field.ReadOnly)
            {
                errors.Add(SystemSettingsErrors.UnknownField(path));
                continue;
            }

            SystemSettingsValueValidator.ValidateValue(field, value, errors);
        }

        // The save replaces the section's whole override row, so a field the payload
        // omits falls back to the baseline (every layer but the database).
        SystemSettingsValueValidator.ValidateSectionRules(
            section, flattened, errors, fullKey => _configuration[fullKey], request.RequestOrigin, _startupSnapshot.Baseline,
            isProduction: _environment?.IsProduction == true);

        if (errors.Count > 0)
        {
            return errors;
        }

        // PK3 (S20 PR C, T-C9), before anything is read for the write or written: a new RP ID,
        // the empty one included, is refused while live passkeys are bound to another.
        if (await _passkeyRpIdChange.RefusalAsync(
                section, flattened, _startupSnapshot.Baseline, fullKey => _configuration[fullKey], cancellationToken)
            is { } rpIdRefusal)
        {
            return rpIdRefusal;
        }

        byte[]? expectedRowVersion = null;
        if (request.RowVersion is not null)
        {
            try
            {
                expectedRowVersion = Convert.FromBase64String(request.RowVersion);
            }
            catch (FormatException)
            {
                return SystemSettingsErrors.ConcurrencyConflict;
            }
        }

        var existing = await _systemSettingsRepository.GetAsync(section.Key, cancellationToken);

        // A stale view of "row exists / doesn't exist" is the same conflict
        // as a stale rowversion.
        if (existing is null != (expectedRowVersion is null))
        {
            return SystemSettingsErrors.ConcurrencyConflict;
        }

        // Before the save, against the limit in force: raising the browser-session
        // idle limit, or switching it off, must not bring idle sessions back
        // (OI-154). A failure leaves the section unchanged.
        await _idleLimitChange.EndSessionsIdleBeforeChangeAsync(
            section, flattened, _startupSnapshot.Baseline, cancellationToken);

        var oldJson = existing?.OverridesJson ?? "{}";
        var newJson = JsonSerializer.Serialize(request.Overrides);

        SystemSettingsOverride entity;
        if (existing is null)
        {
            entity = SystemSettingsOverride.Create(section.Key, newJson, request.UpdatedBy);
        }
        else
        {
            existing.Update(newJson, request.UpdatedBy);
            entity = existing;
        }

        var upsert = await _systemSettingsRepository.UpsertAsync(entity, expectedRowVersion, cancellationToken);
        if (!upsert.Success)
        {
            return SystemSettingsErrors.ConcurrencyConflict;
        }

        await _publisher.Publish(
            new SystemSettingsUpdatedEvent(section.Key, oldJson, newJson, request.UpdatedBy),
            cancellationToken);

        // Same-process direct reload: IOptionsMonitor/IOptionsSnapshot
        // consumers rebind through the configuration change token.
        _reloader.Reload();

        _logger.LogInformation(
            "System settings section {SectionKey} updated by {UpdatedBy} (save #{Version})",
            section.Key, request.UpdatedBy, upsert.Version);

        var saved = new SystemSettingsOverride(
            section.Key, newJson, upsert.Version ?? entity.Version, entity.ModifiedAt, entity.ModifiedBy, upsert.RowVersion);

        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, [saved.ModifiedBy], cancellationToken);

        return SystemSettingsProjector.BuildSection(
            section,
            saved,
            _configuration,
            _startupSnapshot,
            saved.ModifiedBy is { } modifier ? modifierNames.GetValueOrDefault(modifier) : null);
    }
}
