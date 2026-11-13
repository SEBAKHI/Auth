using Auth.Application.DTOs;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.SystemSettings.ResetSystemSettings;

/// <summary>
/// Handler for resetting a section to file values by deleting its override
/// row, then reloading the in-process configuration layer.
/// </summary>
public class ResetSystemSettingsCommandHandler : IRequestHandler<ResetSystemSettingsCommand, ErrorOr<SystemSettingsSectionDto>>
{
    private readonly ISystemSettingsRepository _systemSettingsRepository;
    private readonly IConfiguration _configuration;
    private readonly IStartupValuesSnapshot _startupSnapshot;
    private readonly ISystemSettingsReloader _reloader;
    private readonly IPublisher _publisher;
    private readonly IIdpSessionIdleLimitChange _idleLimitChange;
    private readonly IPasskeyRpIdChange _passkeyRpIdChange;
    private readonly ILogger<ResetSystemSettingsCommandHandler> _logger;

    // Resolved from the container in every host (registered by Program.cs); a test that builds the
    // handler without one gets the rules of an environment that is not Production.
    private readonly IEnvironmentInfo? _environment;

    public ResetSystemSettingsCommandHandler(
        ISystemSettingsRepository systemSettingsRepository,
        IConfiguration configuration,
        IStartupValuesSnapshot startupSnapshot,
        ISystemSettingsReloader reloader,
        IPublisher publisher,
        IIdpSessionIdleLimitChange idleLimitChange,
        IPasskeyRpIdChange passkeyRpIdChange,
        ILogger<ResetSystemSettingsCommandHandler> logger,
        IEnvironmentInfo? environment = null)
    {
        _systemSettingsRepository = systemSettingsRepository;
        _configuration = configuration;
        _startupSnapshot = startupSnapshot;
        _reloader = reloader;
        _publisher = publisher;
        _idleLimitChange = idleLimitChange;
        _passkeyRpIdChange = passkeyRpIdChange;
        _logger = logger;
        _environment = environment;
    }

    public async Task<ErrorOr<SystemSettingsSectionDto>> Handle(ResetSystemSettingsCommand request, CancellationToken cancellationToken)
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

        var existing = await _systemSettingsRepository.GetAsync(section.Key, cancellationToken);
        if (existing is not null)
        {
            // A reset is a write like any other: it swaps the running values for
            // the file ones, and those can be a combination the save path
            // refuses (Email:Enabled=true with a relative FrontendBaseUrl is the
            // documented example — the override was what kept the boot rule
            // satisfied). Validating the POST-reset state first means the console
            // can never delete its way into a configuration that aborts the next
            // restart. The state after the reset is the pre-database baseline, so
            // that is what the rules resolve against, with an empty payload. A rule
            // that reads another section reads it as it runs now: this reset does
            // not change it.
            var resetErrors = new List<Error>();
            SystemSettingsValueValidator.ValidateSectionRules(
                section, [], resetErrors, _startupSnapshot.Baseline, request.RequestOrigin,
                liveValue: fullKey => _configuration[fullKey],
                isProduction: _environment?.IsProduction == true);

            if (resetErrors.Count > 0)
            {
                return resetErrors;
            }

            // PK3 (S20 PR C, T-C9): the file value a reset brings back is a new RP ID like any
            // other, and is refused the same way while live passkeys are bound to another.
            if (await _passkeyRpIdChange.RefusalAsync(
                    section, [], _startupSnapshot.Baseline, fullKey => _configuration[fullKey], cancellationToken)
                is { } rpIdRefusal)
            {
                return rpIdRefusal;
            }

            // The file values may raise the idle limit or switch it off (OI-154).
            await _idleLimitChange.EndSessionsIdleBeforeChangeAsync(
                section, [], _startupSnapshot.Baseline, cancellationToken);

            await _systemSettingsRepository.DeleteAsync(section.Key, cancellationToken);

            await _publisher.Publish(
                new SystemSettingsUpdatedEvent(section.Key, existing.OverridesJson, "{}", request.UpdatedBy),
                cancellationToken);

            _reloader.Reload();

            _logger.LogInformation(
                "System settings section {SectionKey} reset to file values by {UpdatedBy}",
                section.Key, request.UpdatedBy);
        }

        return SystemSettingsProjector.BuildSection(
            section, row: null, _configuration, _startupSnapshot, modifiedByName: null);
    }
}
