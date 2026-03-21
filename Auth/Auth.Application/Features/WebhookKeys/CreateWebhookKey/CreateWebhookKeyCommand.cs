using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.CreateWebhookKey;

/// <summary>
/// Command to create a new webhook key.
/// </summary>
public record CreateWebhookKeyCommand(
    Guid ApplicationId,
    string Name,
    string TargetUrl,
    string? Description = null,
    string Environment = "production",
    DateTime? ExpiresAt = null) : IRequest<ErrorOr<CreateWebhookKeyResponse>>
{
    /// <summary>
    /// The ID of the user creating this webhook key (for audit).
    /// </summary>
    public Guid CreatedBy { get; init; }
}
