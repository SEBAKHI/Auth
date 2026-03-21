using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.ValidateApiKey;

/// <summary>
/// Query to validate an API key and return its metadata.
/// </summary>
public record ValidateApiKeyQuery(string RawApiKey) : IRequest<ErrorOr<ValidateApiKeyResponse>>;
