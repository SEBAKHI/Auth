using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.RevokeWebhookKey;

/// <summary>
/// Command to revoke a webhook key.
/// </summary>
public record RevokeWebhookKeyCommand(
    Guid Id,
    string? Reason = null) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the user revoking this webhook key (for audit).
    /// </summary>
    public Guid RevokedBy { get; init; }
}
