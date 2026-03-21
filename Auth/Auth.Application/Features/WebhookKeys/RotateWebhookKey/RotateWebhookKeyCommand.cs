using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.RotateWebhookKey;

/// <summary>
/// Command to rotate a webhook key.
/// </summary>
public record RotateWebhookKeyCommand(
    Guid WebhookKeyId,
    int GracePeriodMinutes,
    Guid RotatedBy) : IRequest<ErrorOr<RotateWebhookKeyResponse>>;
