using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.DeleteCustomSecret;

/// <summary>
/// Handler for deleting a custom secret.
/// </summary>
public class DeleteCustomSecretCommandHandler : IRequestHandler<DeleteCustomSecretCommand, ErrorOr<Success>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly ILogger<DeleteCustomSecretCommandHandler> _logger;

    public DeleteCustomSecretCommandHandler(
        IDpapiSecretService secretService,
        ILogger<DeleteCustomSecretCommandHandler> logger)
    {
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        DeleteCustomSecretCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var removed = await _secretService.RemoveSecretAsync($"Custom:{request.Key}", cancellationToken);

            if (!removed)
            {
                return SecretErrors.SecretNotFound(request.Key);
            }

            _logger.LogInformation(
                "Custom secret {Key} deleted by user {UserId}",
                request.Key,
                request.RequestedBy);

            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file while deleting custom secret {Key}", request.Key);
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file while deleting custom secret {Key}", request.Key);
            return SecretErrors.FileAccessFailed;
        }
    }
}
