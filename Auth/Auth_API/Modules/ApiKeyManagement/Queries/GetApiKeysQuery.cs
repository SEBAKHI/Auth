using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApiKeyManagement.Queries;

/// <summary>
/// Query to get API keys for an application.
/// </summary>
public record GetApiKeysQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<ApiKeyDto>>>;
