using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.RevokeApiKey;

/// <summary>
/// Handler for revoking an API key.
/// </summary>
public class RevokeApiKeyCommandHandler : IRequestHandler<RevokeApiKeyCommand, ErrorOr<Success>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<RevokeApiKeyCommandHandler> _logger;

    public RevokeApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IPublisher publisher,
        ILogger<RevokeApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(request.Id, cancellationToken);
        if (apiKey == null)
        {
            return ApiKeyErrors.NotFound;
        }

        if (apiKey.IsRevoked)
        {
            return ApiKeyErrors.AlreadyRevoked;
        }

        apiKey.Revoke(request.RevokedBy, request.Reason);
        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);

        _logger.LogInformation(
            "API key revoked: {ApiKeyId} by {RevokedBy}. Reason: {Reason}",
            request.Id, request.RevokedBy, request.Reason ?? "Not specified");

        await _publisher.Publish(
            new ApiKeyRevokedEvent(request.Id, apiKey.ApplicationId, request.RevokedBy),
            cancellationToken);

        return Result.Success;
    }
}
