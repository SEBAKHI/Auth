using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Query to list API keys, optionally narrowed to one application.
/// </summary>
/// <remarks>
/// A null <see cref="ApplicationId"/> spans every application. That is what lets the
/// dashboard link straight to the keys it warned about instead of dropping the reader
/// on an empty page with an application picker.
/// </remarks>
public record GetApiKeysQuery(
    Guid? ApplicationId = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<ApiKeyDto>>>;
