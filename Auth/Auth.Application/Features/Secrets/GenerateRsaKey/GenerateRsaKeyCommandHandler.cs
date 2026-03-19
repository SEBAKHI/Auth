using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateRsaKey;

/// <summary>
/// Handler for regenerating the RSA key pair.
/// </summary>
public class GenerateRsaKeyCommandHandler : IRequestHandler<GenerateRsaKeyCommand, ErrorOr<string>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly ILogger<GenerateRsaKeyCommandHandler> _logger;

    public GenerateRsaKeyCommandHandler(
        IDpapiSecretService secretService,
        ILogger<GenerateRsaKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> Handle(
        GenerateRsaKeyCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                "RSA key regeneration requested by user {UserId} - all access tokens will be invalidated",
                request.RequestedBy);

            var publicKeyPem = await _secretService.GenerateRsaKeyPairAsync(cancellationToken);
            return publicKeyPem;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to generate RSA key pair");
            return SecretErrors.KeyGenerationFailed;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during RSA key generation");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during RSA key generation");
            return SecretErrors.FileAccessFailed;
        }
    }
}
