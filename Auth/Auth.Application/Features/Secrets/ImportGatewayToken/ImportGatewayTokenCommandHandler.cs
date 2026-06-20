using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportGatewayToken;

/// <summary>
/// Handler for importing a caller-supplied gateway token, persisting it to the encrypted secrets file.
/// </summary>
public class ImportGatewayTokenCommandHandler : IRequestHandler<ImportGatewayTokenCommand, ErrorOr<Success>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretManagementSettings _settings;
    private readonly ILogger<ImportGatewayTokenCommandHandler> _logger;

    public ImportGatewayTokenCommandHandler(
        IDpapiSecretService secretService,
        IOptions<SecretManagementSettings> settings,
        ILogger<ImportGatewayTokenCommandHandler> logger)
    {
        _secretService = secretService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ImportGatewayTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        try
        {
            _logger.LogWarning(
                "Gateway token import requested by user {UserId} - the API Gateway must be reconfigured with the same token",
                request.RequestedBy);

            await _secretService.ImportGatewayTokenAsync(request.Token, cancellationToken);
            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during gateway token import");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during gateway token import");
            return SecretErrors.FileAccessFailed;
        }
    }
}
