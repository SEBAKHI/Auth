using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.GetWebhookKeys;

/// <summary>
/// Query to list webhook keys, optionally narrowed to one application.
/// </summary>
/// <remarks>
/// A null <see cref="ApplicationId"/> spans every application, mirroring
/// <see cref="ApiKeys.GetApiKeys.GetApiKeysQuery"/>.
/// </remarks>
public record GetWebhookKeysQuery(
    Guid? ApplicationId = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<WebhookKeyDto>>>;
