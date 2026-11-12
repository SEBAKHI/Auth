using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.TurnstileSecret;

/// <summary>
/// Sets or clears Cloudflare Turnstile's secret key in the encrypted secrets file (S13).
/// </summary>
/// <remarks>
/// <para>
/// The value is issued by Cloudflare and only transcribed here, like the Google client secret: a
/// plain write gated by <c>secrets.manage</c>, with no step-up confirmation. A wrong value cannot
/// let anyone in; it makes the check unable to judge, and the fail mode decides.
/// </para>
/// <para>
/// Refused in PlainText storage, where there is no encrypted file to write to, and, in
/// Production, refused when it is one of Cloudflare's testing secrets, which accept every token.
/// Takes effect on the next API restart: the secrets file is read into configuration once, at
/// startup. The audit trail records the key name, never the value.
/// </para>
/// </remarks>
public class TurnstileSecretCommandHandler :
    IRequestHandler<SetTurnstileSecretCommand, ErrorOr<Success>>,
    IRequestHandler<ClearTurnstileSecretCommand, ErrorOr<Success>>
{
    /// <summary>The storage key, as the secrets status reports it.</summary>
    public const string SecretKey = "TurnstileSecretKey";

    private readonly IDpapiSecretService _secretService;
    private readonly SecretManagementSettings _settings;
    private readonly IEnvironmentInfo _environment;
    private readonly IPublisher _publisher;
    private readonly ILogger<TurnstileSecretCommandHandler> _logger;

    public TurnstileSecretCommandHandler(
        IDpapiSecretService secretService,
        IOptions<SecretManagementSettings> settings,
        IEnvironmentInfo environment,
        IPublisher publisher,
        ILogger<TurnstileSecretCommandHandler> logger)
    {
        _secretService = secretService;
        _settings = settings.Value;
        _environment = environment;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<ErrorOr<Success>> Handle(SetTurnstileSecretCommand request, CancellationToken cancellationToken)
    {
        // Trimmed: a key pasted with a stray space or line break would be refused by Cloudflare
        // only after the restart.
        var value = request.Value.Trim();
        if (!_settings.IsPlainTextMode && _environment.IsProduction && TurnstileTestingKeys.IsTestingKey(value))
        {
            // PlainText storage answers first (in WriteAsync): there is no file to judge a value for.
            return Task.FromResult<ErrorOr<Success>>(BotDefenseErrors.TestingKeyInProduction);
        }

        return WriteAsync(value, request.RequestedBy, "stored", cancellationToken);
    }

    public Task<ErrorOr<Success>> Handle(ClearTurnstileSecretCommand request, CancellationToken cancellationToken) =>
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
                    secrets.TurnstileSecretKey = value;
                    return Result.Success;
                },
                overwriteBackup: false,
                cancellationToken);

            if (saved.IsError)
            {
                return saved.Errors;
            }

            _logger.LogInformation(
                "Turnstile secret key {Verb} in the encrypted secrets file by user {UserId}. " +
                "It takes effect on the next API restart; back the secrets file up now.",
                verb, requestedBy);

            await PublishAuditAsync(requestedBy, cancellationToken);

            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file while writing the Turnstile secret key");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file while writing the Turnstile secret key");
            return SecretErrors.FileAccessFailed;
        }
    }

    /// <summary>
    /// Writes the audit trail without letting it fail the request: the file is already saved, and
    /// a database outage reported as a failed write would tell the operator the opposite of what
    /// happened (the same rule as the Google client secret's handler).
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
