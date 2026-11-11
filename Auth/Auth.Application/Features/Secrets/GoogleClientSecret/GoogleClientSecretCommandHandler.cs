using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.GoogleClientSecret;

/// <summary>
/// Sets or clears Google's OAuth client secret in the encrypted secrets file.
/// </summary>
/// <remarks>
/// <para>
/// Like the SMTP password and the connection string, the value is owned by an outside party
/// (Google Cloud Console), so the system only transcribes it: a plain write, gated by
/// <c>secrets.manage</c>, with no step-up confirmation. A wrong value cannot sign anyone in;
/// the worst it does is make the Google sign-in fail until it is corrected.
/// </para>
/// <para>
/// It takes effect on the next API restart. The secrets file is read into configuration once,
/// at startup, and the code exchange reads the value from configuration.
/// </para>
/// <para>
/// The audit trail records the key name, never the value (<see cref="SecretValueChangedEvent"/>).
/// The value is write-only: <see cref="IDpapiSecretService"/> has no key name that reads it
/// back, and the status reports only whether it is set.
/// </para>
/// </remarks>
public class GoogleClientSecretCommandHandler :
    IRequestHandler<SetGoogleClientSecretCommand, ErrorOr<Success>>,
    IRequestHandler<ClearGoogleClientSecretCommand, ErrorOr<Success>>
{
    /// <summary>The storage key, as the secrets status reports it.</summary>
    public const string SecretKey = "GoogleClientSecret";

    private readonly IDpapiSecretService _secretService;
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<GoogleClientSecretCommandHandler> _logger;

    public GoogleClientSecretCommandHandler(
        IDpapiSecretService secretService,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<GoogleClientSecretCommandHandler> logger)
    {
        _secretService = secretService;
        _settings = settings.Value;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<ErrorOr<Success>> Handle(
        SetGoogleClientSecretCommand request,
        CancellationToken cancellationToken) =>
        // Trimmed: a secret pasted from Google Cloud Console with a stray space or line break
        // would be stored as typed and refused by Google (invalid_client) only after a restart.
        WriteAsync(request.Value.Trim(), request.RequestedBy, "stored", cancellationToken);

    public Task<ErrorOr<Success>> Handle(
        ClearGoogleClientSecretCommand request,
        CancellationToken cancellationToken) =>
        WriteAsync(null, request.RequestedBy, "removed", cancellationToken);

    private async Task<ErrorOr<Success>> WriteAsync(
        string? value,
        Guid requestedBy,
        string verb,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.SetNotSupportedInPlainText;
        }

        try
        {
            var saved = await _secretService.UpdateAsync<Success>(
                secrets =>
                {
                    secrets.GoogleClientSecret = value;
                    return Result.Success;
                },
                overwriteBackup: false,
                cancellationToken);

            if (saved.IsError)
            {
                return saved.Errors;
            }

            _logger.LogInformation(
                "Google client secret {Verb} in the encrypted secrets file by user {UserId}. " +
                "It takes effect on the next API restart.",
                verb, requestedBy);

            await PublishAuditAsync(requestedBy, cancellationToken);

            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file while writing the Google client secret");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file while writing the Google client secret");
            return SecretErrors.FileAccessFailed;
        }
    }

    /// <summary>
    /// Writes the audit trail without letting it fail the request: the file is already saved,
    /// and a database outage reported as a failed write would tell the operator the opposite
    /// of what happened (the same rule as the SMTP password's handler).
    /// </summary>
    private async Task PublishAuditAsync(Guid requestedBy, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(new SecretValueChangedEvent(SecretKey, requestedBy), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SecretKey} was written successfully by user {UserId} but the audit row could not be " +
                "written. This log line is the record of that change.",
                SecretKey, requestedBy);
        }
    }
}
