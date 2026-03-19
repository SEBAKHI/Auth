using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateGatewayToken;

/// <summary>
/// Handler for regenerating the gateway token.
/// </summary>
public class GenerateGatewayTokenCommandHandler : IRequestHandler<GenerateGatewayTokenCommand, ErrorOr<string>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly ILogger<GenerateGatewayTokenCommandHandler> _logger;

    public GenerateGatewayTokenCommandHandler(
        IDpapiSecretService secretService,
        ILogger<GenerateGatewayTokenCommandHandler> logger)
    {
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> Handle(
        GenerateGatewayTokenCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                "Gateway token regeneration requested by user {UserId}",
                request.RequestedBy);

            var token = await _secretService.GenerateGatewayTokenAsync(cancellationToken);
            return token;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to generate gateway token");
            return SecretErrors.KeyGenerationFailed;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during gateway token generation");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during gateway token generation");
            return SecretErrors.FileAccessFailed;
        }
    }
}
