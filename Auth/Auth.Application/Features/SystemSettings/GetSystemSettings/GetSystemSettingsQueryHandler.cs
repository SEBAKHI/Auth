using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Auth.Application.Features.SystemSettings.GetSystemSettings;

/// <summary>
/// Handler for reading the full system-settings view.
/// </summary>
public class GetSystemSettingsQueryHandler : IRequestHandler<GetSystemSettingsQuery, ErrorOr<SystemSettingsDto>>
{
    private readonly ISystemSettingsRepository _systemSettingsRepository;
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly IStartupValuesSnapshot _startupSnapshot;
    private readonly ISystemSettingsReloader _reloader;

    public GetSystemSettingsQueryHandler(
        ISystemSettingsRepository systemSettingsRepository,
        IUserRepository userRepository,
        IConfiguration configuration,
        IStartupValuesSnapshot startupSnapshot,
        ISystemSettingsReloader reloader)
    {
        _systemSettingsRepository = systemSettingsRepository;
        _userRepository = userRepository;
        _configuration = configuration;
        _startupSnapshot = startupSnapshot;
        _reloader = reloader;
    }

    public async Task<ErrorOr<SystemSettingsDto>> Handle(GetSystemSettingsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<SystemSettingsOverride> rows;
        try
        {
            rows = await _systemSettingsRepository.GetAllAsync(cancellationToken);
        }
        catch
        {
            // The page must stay readable while the database is down — the
            // provider already fails open the same way at startup.
            rows = [];
        }

        var rowsByKey = rows.ToDictionary(r => r.SectionKey, StringComparer.OrdinalIgnoreCase);
        var modifierNames = await NameLookupHelper.UserNamesAsync(
            _userRepository, rows.Select(r => r.ModifiedBy), cancellationToken);

        var sections = SystemSettingsRegistry.Sections
            .Select(definition =>
            {
                var row = rowsByKey.GetValueOrDefault(definition.Key);
                var modifiedByName = row?.ModifiedBy is { } modifier
                    ? modifierNames.GetValueOrDefault(modifier)
                    : null;
                return SystemSettingsProjector.BuildSection(
                    definition, row, _configuration, _startupSnapshot, modifiedByName);
            })
            .ToList();

        return new SystemSettingsDto
        {
            RestartPending = sections.Any(s => s.Fields.Any(f => f.IsPendingRestart)),
            DbOverridesUnavailable = _reloader.LastLoadFailed,
            Sections = sections
        };
    }
}
