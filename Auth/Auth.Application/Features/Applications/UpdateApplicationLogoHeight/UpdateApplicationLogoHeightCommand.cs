using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.UpdateApplicationLogoHeight;

/// <summary>
/// Sets the height an application's logo is shown at on its sign-in pages. The image is not
/// touched.
/// </summary>
/// <remarks>
/// Not a field on the update contract, for the reason given at <c>SetApplicationActiveCommand</c>:
/// the console's update is a full-object PUT built from possibly stale client state.
/// </remarks>
/// <param name="ApplicationId">The application.</param>
/// <param name="LogoHeightPx">The height in pixels, or null to go back to the default.</param>
public sealed record UpdateApplicationLogoHeightCommand(
    Guid ApplicationId,
    int? LogoHeightPx) : IRequest<ErrorOr<ApplicationLogoHeightDto>>
{
    /// <summary>The administrator saving it.</summary>
    public Guid ChangedBy { get; init; }
}
