using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.DeactivateLoginDesign;

/// <summary>
/// Stops showing a design without activating another: an application's pages fall back to
/// the platform's active design, the platform's to the plain panel.
/// </summary>
/// <param name="ApplicationId">The scope: the application, or null for the platform.</param>
/// <param name="DesignId">The design to deactivate.</param>
public sealed record DeactivateLoginDesignCommand(Guid? ApplicationId, Guid DesignId) : IRequest<ErrorOr<LoginDesignDto>>
{
    /// <summary>The administrator deactivating it.</summary>
    public Guid DeactivatedBy { get; init; }
}
