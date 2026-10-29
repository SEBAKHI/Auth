using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.CreateLoginDesign;

/// <summary>
/// Creates a design in a scope. It starts inactive: no visitor sees it until it is activated.
/// </summary>
/// <param name="ApplicationId">The application; null for one of the platform's designs.</param>
/// <param name="Name">The design's name.</param>
/// <param name="Content">Its HTML; null starts from <c>LoginDesignStarter</c>.</param>
/// <param name="StringsJson">Its per-language strings; null with no content starts from the starter's.</param>
/// <param name="ShowPlatformLogo">Whether the platform's mark keeps the corner.</param>
public sealed record CreateLoginDesignCommand(
    Guid? ApplicationId,
    string Name,
    string? Content,
    string? StringsJson,
    bool ShowPlatformLogo = true) : IRequest<ErrorOr<LoginDesignDto>>
{
    /// <summary>The administrator creating it.</summary>
    public Guid CreatedBy { get; init; }
}
