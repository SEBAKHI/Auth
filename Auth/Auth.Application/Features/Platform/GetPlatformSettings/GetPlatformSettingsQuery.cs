using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.GetPlatformSettings;

/// <summary>
/// Query for the full platform settings (admin view).
/// </summary>
public record GetPlatformSettingsQuery() : IRequest<ErrorOr<PlatformSettingsDto>>;
