using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.ValidateWebhookKey;

/// <summary>
/// Query to validate a webhook key and return its metadata.
/// </summary>
public record ValidateWebhookKeyQuery(string RawWebhookKey) : IRequest<ErrorOr<ValidateWebhookKeyResponse>>;
