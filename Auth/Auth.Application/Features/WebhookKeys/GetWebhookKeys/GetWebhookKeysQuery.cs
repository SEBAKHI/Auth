using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.GetWebhookKeys;

/// <summary>
/// Query to get webhook keys for an application.
/// </summary>
public record GetWebhookKeysQuery(
    Guid ApplicationId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<WebhookKeyDto>>>;
