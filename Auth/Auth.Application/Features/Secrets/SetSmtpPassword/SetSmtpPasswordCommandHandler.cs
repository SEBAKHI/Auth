using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.SetSmtpPassword;

/// <summary>
/// Stores the SMTP password in the encrypted secrets file, from where it
/// overrides <c>Email:Password</c> for every configuration layer beneath it.
/// </summary>
/// <remarks>
/// Deliberately not gated behind a step-up confirmation, unlike the key
/// rotations on this controller. That confirmation is delivered by email: making
/// it a prerequisite for repairing a broken SMTP password would mean the code
/// can only arrive once the thing it authorizes has already been fixed.
/// <para>
/// The value is also not verified against the mail server here. The running
/// process keeps its startup configuration until it restarts, so a send attempt
/// now would exercise the <em>previous</em> password and report a result that
/// says nothing about the value being stored.
/// </para>
/// </remarks>
public class SetSmtpPasswordCommandHandler : IRequestHandler<SetSmtpPasswordCommand, ErrorOr<Success>>
{
    private const string SecretKey = "SmtpPassword";

    private readonly IDpapiSecretService _secretService;
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<SetSmtpPasswordCommandHandler> _logger;

    public SetSmtpPasswordCommandHandler(
        IDpapiSecretService secretService,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<SetSmtpPasswordCommandHandler> logger)
    {
        _secretService = secretService;
        _settings = settings.Value;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        SetSmtpPasswordCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.SetNotSupportedInPlainText;
        }

        try
        {
            await _secretService.SetSecretAsync(SecretKey, request.Value, cancellationToken);

            _logger.LogInformation(
                "SMTP password stored in the encrypted secrets file by user {UserId}. " +
                "It takes effect on the next API restart.",
                request.RequestedBy);

            await PublishAuditAsync(request.RequestedBy, cancellationToken);

            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file while storing the SMTP password");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file while storing the SMTP password");
            return SecretErrors.FileAccessFailed;
        }
    }

    /// <summary>
    /// Writes the audit trail without letting it fail the request.
    /// </summary>
    /// <remarks>
    /// The audit handler inserts a row through the database, and the secret has
    /// already been written to disk by the time it runs. Letting a database
    /// outage turn a completed write into a 500 would tell the operator the
    /// opposite of what happened. The failure is logged at Error with the key
    /// name instead, which reaches the durable Serilog file either way.
    /// </remarks>
    private async Task PublishAuditAsync(Guid requestedBy, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(
                new SecretValueChangedEvent(SecretKey, requestedBy), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SecretKey} was stored successfully by user {UserId} but the audit row could not be " +
                "written. This log line is the record of that change.",
                SecretKey, requestedBy);
        }
    }
}
