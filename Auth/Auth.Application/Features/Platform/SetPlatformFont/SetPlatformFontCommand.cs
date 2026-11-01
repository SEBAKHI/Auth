using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Platform.SetPlatformFont;

/// <summary>
/// A font file as the request carried it.
/// </summary>
/// <param name="Content">The file's bytes; read once, by the storage service.</param>
/// <param name="LengthBytes">The file's length as the request declared it, checked before any read.</param>
/// <param name="FileName">The name the browser sent; used only as the display name of a font that declares none.</param>
public sealed record FontUpload(Stream Content, long LengthBytes, string? FileName);

/// <summary>
/// Command to set one of the platform's fonts: a new WOFF2 file for a role, or none (back to
/// the shipped IBM Plex Sans). Nothing else in the settings changes: fonts are saved on their
/// own, as the appearance is, so neither can put back a stale copy of the other.
/// </summary>
/// <param name="Role">Which font: heading or body.</param>
/// <param name="Upload">The new file, or null to remove the role's font.</param>
/// <param name="UpdatedBy">ID of the admin performing the update.</param>
public record SetPlatformFontCommand(
    FontRole Role,
    FontUpload? Upload,
    Guid UpdatedBy) : IRequest<ErrorOr<PlatformSettingsDto>>;
