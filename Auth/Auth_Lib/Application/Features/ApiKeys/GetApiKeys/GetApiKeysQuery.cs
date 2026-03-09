using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Query to get API keys for an application.
/// </summary>
public record GetApiKeysQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<ApiKeyDto>>>;
