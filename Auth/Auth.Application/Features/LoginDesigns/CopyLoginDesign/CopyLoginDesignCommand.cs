using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.CopyLoginDesign;

/// <summary>
/// Copies a design into a new one in the same scope. The copy starts inactive, whatever the
/// original's state, so copying the active design to rework it changes nothing visitors see.
/// </summary>
/// <param name="ApplicationId">The scope: the application, or null for the platform.</param>
/// <param name="DesignId">The design to copy.</param>
/// <param name="Name">The copy's name.</param>
public sealed record CopyLoginDesignCommand(
    Guid? ApplicationId,
    Guid DesignId,
    string Name) : IRequest<ErrorOr<LoginDesignDto>>
{
    /// <summary>The administrator copying it.</summary>
    public Guid CreatedBy { get; init; }
}
