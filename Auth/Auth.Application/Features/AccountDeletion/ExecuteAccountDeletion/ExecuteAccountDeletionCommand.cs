using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.ExecuteAccountDeletion;

/// <summary>
/// Command to execute the staged, irreversible destruction of an account
/// whose grace window has elapsed. Sent only by the deletion worker.
/// </summary>
/// <param name="RequestId">The deletion request to execute.</param>
public record ExecuteAccountDeletionCommand(Guid RequestId) : IRequest<ErrorOr<Success>>;
