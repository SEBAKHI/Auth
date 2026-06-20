using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportHmacKey;

/// <summary>
/// Handler for importing a caller-supplied HMAC key. Validates that the value is base64 and at
/// least 256 bits, then persists it to the encrypted secrets file.
/// </summary>
public class ImportHmacKeyCommandHandler : IRequestHandler<ImportHmacKeyCommand, ErrorOr<Success>>
{
    private const int MinimumHmacKeyBytes = 32; // 256-bit minimum

    private readonly IDpapiSecretService _secretService;
    private readonly SecretManagementSettings _settings;
    private readonly ILogger<ImportHmacKeyCommandHandler> _logger;

    public ImportHmacKeyCommandHandler(
        IDpapiSecretService secretService,
        IOptions<SecretManagementSettings> settings,
        ILogger<ImportHmacKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ImportHmacKeyCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(request.HmacKeyBase64);
        }
        catch (FormatException)
        {
            return SecretErrors.InvalidKeyMaterial("The HMAC key must be a valid Base64 string.");
        }

        if (decoded.Length < MinimumHmacKeyBytes)
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The HMAC key must decode to at least {MinimumHmacKeyBytes} bytes (256 bits).");
        }

        try
        {
            _logger.LogWarning(
                "HMAC key import requested by user {UserId} - all existing refresh tokens will be invalidated",
                request.RequestedBy);

            await _secretService.ImportHmacKeyAsync(request.HmacKeyBase64, cancellationToken);
            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during HMAC key import");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during HMAC key import");
            return SecretErrors.FileAccessFailed;
        }
    }
}
