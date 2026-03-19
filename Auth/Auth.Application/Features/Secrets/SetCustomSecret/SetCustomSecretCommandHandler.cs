using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.SetCustomSecret;

/// <summary>
/// Handler for setting a custom secret value.
/// </summary>
public class SetCustomSecretCommandHandler : IRequestHandler<SetCustomSecretCommand, ErrorOr<Success>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly ILogger<SetCustomSecretCommandHandler> _logger;

    public SetCustomSecretCommandHandler(
        IDpapiSecretService secretService,
        ILogger<SetCustomSecretCommandHandler> logger)
    {
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        SetCustomSecretCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _secretService.SetSecretAsync($"Custom:{request.Key}", request.Value, cancellationToken);

            _logger.LogInformation(
                "Custom secret {Key} set by user {UserId}",
                request.Key,
                request.RequestedBy);

            return Result.Success;
        }
        catch (ArgumentException)
        {
            return SecretErrors.UnknownSecretKey(request.Key);
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file while setting custom secret {Key}", request.Key);
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file while setting custom secret {Key}", request.Key);
            return SecretErrors.FileAccessFailed;
        }
    }
}
