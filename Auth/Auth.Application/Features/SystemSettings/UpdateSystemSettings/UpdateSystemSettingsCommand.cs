using System.Text.Json;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.UpdateSystemSettings;

/// <summary>
/// Replaces one section's override set with the given sparse nested-JSON
/// object (fields omitted from the payload revert to file values).
/// </summary>
/// <param name="SectionKey">Registry section key (e.g. "Jwt").</param>
/// <param name="Overrides">The complete new override object for the section.</param>
/// <param name="RowVersion">
/// Base64 rowversion the client last read; null when no override row existed.
/// A mismatch fails with a concurrency conflict instead of writing.
/// </param>
/// <param name="RequestOrigin">
/// The browser Origin of the page making the change, if any: a first-party list
/// that leaves it out is refused, so a console cannot lock itself out.
/// </param>
public record UpdateSystemSettingsCommand(
    string SectionKey,
    JsonElement Overrides,
    string? RowVersion,
    Guid UpdatedBy,
    string? RequestOrigin = null) : IRequest<ErrorOr<SystemSettingsSectionDto>>;
