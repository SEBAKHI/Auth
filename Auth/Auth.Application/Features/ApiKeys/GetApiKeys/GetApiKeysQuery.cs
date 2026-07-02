using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Query to get API keys for an application.
/// </summary>
public record GetApiKeysQuery(
    Guid ApplicationId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<ApiKeyDto>>>;
