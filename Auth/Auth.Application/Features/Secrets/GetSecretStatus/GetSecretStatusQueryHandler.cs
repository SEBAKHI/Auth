using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GetSecretStatus;

/// <summary>
/// Handler for retrieving secret status.
/// </summary>
public class GetSecretStatusQueryHandler : IRequestHandler<GetSecretStatusQuery, ErrorOr<SecretStatusResult>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly ILogger<GetSecretStatusQueryHandler> _logger;

    public GetSecretStatusQueryHandler(
        IDpapiSecretService secretService,
        ILogger<GetSecretStatusQueryHandler> logger)
    {
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<ErrorOr<SecretStatusResult>> Handle(
        GetSecretStatusQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await _secretService.GetStatusAsync(cancellationToken);
            return status;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file while retrieving status");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to access secret file while retrieving status");
            return SecretErrors.FileAccessFailed;
        }
    }
}
