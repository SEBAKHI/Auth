using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApiKeyManagement.Commands;

/// <summary>
/// Handler for revoking an API key.
/// </summary>
public class RevokeApiKeyCommandHandler : IRequestHandler<RevokeApiKeyCommand, ErrorOr<Success>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly ILogger<RevokeApiKeyCommandHandler> _logger;

    public RevokeApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        ILogger<RevokeApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(request.Id, cancellationToken);
        if (apiKey == null)
        {
            return Error.NotFound(
                code: "ApiKey.NotFound",
                description: "API key not found.");
        }

        if (apiKey.IsRevoked)
        {
            return Error.Conflict(
                code: "ApiKey.AlreadyRevoked",
                description: "API key is already revoked.");
        }

        apiKey.Revoke(request.RevokedBy, request.Reason);
        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken);

        _logger.LogInformation(
            "API key revoked: {ApiKeyId} by {RevokedBy}. Reason: {Reason}",
            request.Id, request.RevokedBy, request.Reason ?? "Not specified");

        return Result.Success;
    }
}
