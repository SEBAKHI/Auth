using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.DeleteLoginDesign;

/// <summary>Deletes an inactive design. The active one is refused (<c>LoginDesign.DeleteActive</c>).</summary>
/// <param name="ApplicationId">The scope: the application, or null for the platform.</param>
/// <param name="DesignId">The design to delete.</param>
public sealed record DeleteLoginDesignCommand(Guid? ApplicationId, Guid DesignId) : IRequest<ErrorOr<Deleted>>
{
    /// <summary>The administrator deleting it.</summary>
    public Guid DeletedBy { get; init; }
}
