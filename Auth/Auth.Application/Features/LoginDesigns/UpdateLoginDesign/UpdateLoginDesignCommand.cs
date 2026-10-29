using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.UpdateLoginDesign;

/// <summary>
/// Saves a design. There is no draft: on the active design the save is what the scope's
/// sign-in pages show next.
/// </summary>
/// <param name="ApplicationId">The scope: the application, or null for the platform.</param>
/// <param name="DesignId">The design.</param>
/// <param name="Name">Its name.</param>
/// <param name="Content">Its HTML.</param>
/// <param name="StringsJson">Its per-language strings.</param>
/// <param name="ShowPlatformLogo">Whether the platform's mark keeps the corner.</param>
/// <param name="ExpectedRevision">The revision the editor loaded (<c>LoginDesignDto.Revision</c>).</param>
public sealed record UpdateLoginDesignCommand(
    Guid? ApplicationId,
    Guid DesignId,
    string Name,
    string Content,
    string? StringsJson,
    bool ShowPlatformLogo,
    DateTime ExpectedRevision) : IRequest<ErrorOr<LoginDesignDto>>
{
    /// <summary>The administrator saving it.</summary>
    public Guid ModifiedBy { get; init; }
}
