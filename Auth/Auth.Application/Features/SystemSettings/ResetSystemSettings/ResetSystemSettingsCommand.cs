using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.ResetSystemSettings;

/// <summary>
/// Removes every stored override of a section so all its fields fall back to
/// the configuration files.
/// </summary>
/// <param name="SectionKey">Registry section key.</param>
/// <param name="UpdatedBy">The administrator making the change.</param>
/// <param name="RequestOrigin">The browser Origin of the page making the change, if any (see UpdateSystemSettingsCommand).</param>
public record ResetSystemSettingsCommand(
    string SectionKey,
    Guid UpdatedBy,
    string? RequestOrigin = null) : IRequest<ErrorOr<SystemSettingsSectionDto>>;
