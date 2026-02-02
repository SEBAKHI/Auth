using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Commands;

/// <summary>
/// Command to add a permission implication (permission A implies permission B).
/// </summary>
public record AddPermissionImplicationCommand(
    Guid PermissionId,
    Guid ImpliedPermissionId) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The ID of the user creating this implication (for audit).
    /// </summary>
    public Guid CreatedBy { get; set; }
}
