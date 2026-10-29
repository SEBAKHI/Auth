using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.ActivateLoginDesign;

/// <summary>
/// Makes a design the one its scope's sign-in pages show; the design active before is
/// deactivated in the same transaction.
/// </summary>
/// <param name="ApplicationId">The scope: the application, or null for the platform.</param>
/// <param name="DesignId">The design to activate.</param>
public sealed record ActivateLoginDesignCommand(Guid? ApplicationId, Guid DesignId) : IRequest<ErrorOr<LoginDesignDto>>
{
    /// <summary>The administrator activating it.</summary>
    public Guid ActivatedBy { get; init; }
}
