using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportRsaKey;

/// <summary>
/// Handler for importing a caller-supplied RSA signing key. Validates the PEM, derives the
/// public key, and persists both to the encrypted secrets file.
/// </summary>
public class ImportRsaKeyCommandHandler : IRequestHandler<ImportRsaKeyCommand, ErrorOr<string>>
{
    private const int MinimumRsaKeySizeBits = 2048;

    private readonly IDpapiSecretService _secretService;
    private readonly SecretManagementSettings _settings;
    private readonly ILogger<ImportRsaKeyCommandHandler> _logger;

    public ImportRsaKeyCommandHandler(
        IDpapiSecretService secretService,
        IOptions<SecretManagementSettings> settings,
        ILogger<ImportRsaKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> Handle(
        ImportRsaKeyCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        string publicKeyPem;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(request.PrivateKeyPem);

            if (rsa.KeySize < MinimumRsaKeySizeBits)
            {
                return SecretErrors.InvalidKeyMaterial(
                    $"RSA key size {rsa.KeySize} is below the required {MinimumRsaKeySizeBits}-bit minimum.");
            }

            // Confirm the PEM actually carries a private key (not just a public key) before storing it.
            _ = rsa.ExportPkcs8PrivateKey();
            publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return SecretErrors.InvalidKeyMaterial(
                "The value is not a valid RSA private key in PEM format (expected PKCS#8 or PKCS#1).");
        }

        try
        {
            _logger.LogWarning(
                "RSA signing key import requested by user {UserId} - all existing access tokens will be invalidated",
                request.RequestedBy);

            await _secretService.ImportRsaKeyPairAsync(request.PrivateKeyPem, publicKeyPem, cancellationToken);
            return publicKeyPem;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during RSA key import");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during RSA key import");
            return SecretErrors.FileAccessFailed;
        }
    }
}
