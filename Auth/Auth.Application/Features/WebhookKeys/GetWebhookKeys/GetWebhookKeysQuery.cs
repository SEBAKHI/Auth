using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.GetWebhookKeys;

/// <summary>
/// Query to get webhook keys for an application.
/// </summary>
public record GetWebhookKeysQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<WebhookKeyDto>>>;
