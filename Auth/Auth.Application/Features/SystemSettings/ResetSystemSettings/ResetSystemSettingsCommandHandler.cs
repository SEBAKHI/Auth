using Auth.Application.DTOs;
using Auth.Application.Features.SystemSettings.Common;
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
    private readonly ILogger<ResetSystemSettingsCommandHandler> _logger;

    public ResetSystemSettingsCommandHandler(
        ISystemSettingsRepository systemSettingsRepository,
        IConfiguration configuration,
        IStartupValuesSnapshot startupSnapshot,
        ISystemSettingsReloader reloader,
        IPublisher publisher,
        ILogger<ResetSystemSettingsCommandHandler> logger)
    {
        _systemSettingsRepository = systemSettingsRepository;
        _configuration = configuration;
        _startupSnapshot = startupSnapshot;
        _reloader = reloader;
        _publisher = publisher;
        _logger = logger;
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
