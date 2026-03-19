using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateHmacKey;

/// <summary>
/// Handler for regenerating the HMAC key.
/// </summary>
public class GenerateHmacKeyCommandHandler : IRequestHandler<GenerateHmacKeyCommand, ErrorOr<Success>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly ILogger<GenerateHmacKeyCommandHandler> _logger;

    public GenerateHmacKeyCommandHandler(
        IDpapiSecretService secretService,
        ILogger<GenerateHmacKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        GenerateHmacKeyCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                "HMAC key regeneration requested by user {UserId} - all refresh tokens will be invalidated",
                request.RequestedBy);

            await _secretService.GenerateHmacKeyAsync(cancellationToken);
            return Result.Success;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to generate HMAC key");
            return SecretErrors.KeyGenerationFailed;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during HMAC key generation");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during HMAC key generation");
            return SecretErrors.FileAccessFailed;
        }
    }
}
