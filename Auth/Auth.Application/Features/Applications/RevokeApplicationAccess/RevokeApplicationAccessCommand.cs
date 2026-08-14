using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.RevokeApplicationAccess;

/// <summary>
/// Withdraws a user's invitation to an application. The row is kept and marked
/// revoked, so the trial stays on the record.
/// </summary>
public record RevokeApplicationAccessCommand(
    Guid ApplicationId,
    Guid UserId) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the administrator withdrawing the invitation (for audit).
    /// </summary>
    public Guid RevokedBy { get; init; }
}
