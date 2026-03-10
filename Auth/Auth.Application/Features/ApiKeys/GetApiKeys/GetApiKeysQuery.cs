using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Query to get API keys for an application.
/// </summary>
public record GetApiKeysQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<ApiKeyDto>>>;
