using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Applications.DeleteApplication;

/// <summary>
/// Command to delete an application.
/// </summary>
public record DeleteApplicationCommand(Guid Id) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The ID of the user deleting this application (for audit).
    /// </summary>
    public Guid DeletedBy { get; set; }
}
