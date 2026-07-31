using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.SystemSettings.GetSystemSettings;

/// <summary>
/// Returns every registry section with per-field effective/override/baseline
/// values, source attribution, and restart-pending flags.
/// </summary>
public record GetSystemSettingsQuery : IRequest<ErrorOr<SystemSettingsDto>>;
