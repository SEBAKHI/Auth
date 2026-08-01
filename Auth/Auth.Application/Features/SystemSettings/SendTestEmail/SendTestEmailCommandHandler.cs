using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.SystemSettings.SendTestEmail;

/// <summary>
/// Handler for the system-settings test-email diagnostic. Bypasses the
/// template pipeline on purpose: the point is to prove the SMTP transport
/// settings, not the notification content system.
/// </summary>
public class SendTestEmailCommandHandler : IRequestHandler<SendTestEmailCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IDirectEmailSender _emailSender;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<SendTestEmailCommandHandler> _logger;

    public SendTestEmailCommandHandler(
        IUserRepository userRepository,
        IDirectEmailSender emailSender,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<SendTestEmailCommandHandler> logger)
    {
        _userRepository = userRepository;
        _emailSender = emailSender;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(SendTestEmailCommand request, CancellationToken cancellationToken)
    {
        if (!_emailSettings.Enabled)
        {
            return SystemSettingsErrors.EmailSendingDisabled;
        }

        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        string? recipient = user?.Email;
        if (string.IsNullOrEmpty(recipient))
        {
            return SystemSettingsErrors.TestEmailFailed("the calling account has no email address.");
        }

        var subject = $"Test email from {_emailSettings.SenderName}";
        var htmlBody =
            $"<p>This is a test email sent from the system settings page.</p>" +
            $"<p>SMTP host: {_emailSettings.SmtpHost}:{_emailSettings.SmtpPort} " +
            $"(SSL: {_emailSettings.UseSsl}), sender: {_emailSettings.SenderEmail}.</p>" +
            "<p>If you received this message, outbound email is configured correctly.</p>";

        var sent = await _emailSender.SendAsync(
            recipient,
            subject,
            htmlBody,
            textBody: "This is a test email sent from the system settings page. " +
                      "If you received this message, outbound email is configured correctly.",
            cancellationToken);

        if (!sent)
        {
            // The transport logs the exception detail; the API answer stays
            // generic (SMTP errors can embed hostnames and credentials).
            return SystemSettingsErrors.TestEmailFailed("the SMTP transport reported a failure — check the server logs.");
        }

        _logger.LogInformation("System-settings test email sent to the requesting administrator ({UserId})", request.UserId);
        return Result.Success;
    }
}
