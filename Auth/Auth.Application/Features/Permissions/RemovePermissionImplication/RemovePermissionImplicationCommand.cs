using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.RemovePermissionImplication;

/// <summary>
/// Command to remove a permission implication.
/// </summary>
public record RemovePermissionImplicationCommand(
    Guid PermissionId,
    Guid ImpliedPermissionId) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The ID of the user removing this implication (for audit).
    /// </summary>
    public Guid RemovedBy { get; init; }
}
