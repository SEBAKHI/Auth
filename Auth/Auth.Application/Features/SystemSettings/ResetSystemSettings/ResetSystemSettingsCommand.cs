using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.ResetSystemSettings;

/// <summary>
/// Removes every stored override of a section so all its fields fall back to
/// the configuration files.
/// </summary>
public record ResetSystemSettingsCommand(
    string SectionKey,
    Guid UpdatedBy) : IRequest<ErrorOr<SystemSettingsSectionDto>>;
