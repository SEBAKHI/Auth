using System.Net;
using System.Net.Mail;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_Lib.Infrastructure.Email;

/// <summary>
/// SMTP implementation of the email service.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IOptions<EmailSettings> settings,
        ILogger<SmtpEmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SendVerificationOtpAsync(
        string toEmail,
        string recipientName,
        string otp,
        int expirationMinutes,
        CancellationToken cancellationToken = default)
    {
        var subject = "Verify Your Email Address";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; margin: 0; padding: 0; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 40px 20px; }}
        .card {{ background: #ffffff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ text-align: center; margin-bottom: 30px; }}
        .header h1 {{ color: #1a1a1a; font-size: 24px; margin: 0; }}
        .otp-container {{ text-align: center; margin: 30px 0; }}
        .otp-code {{ display: inline-block; font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #2563eb; padding: 20px 30px; background: #f3f4f6; border-radius: 8px; font-family: 'Courier New', monospace; }}
        .message {{ color: #4b5563; font-size: 16px; line-height: 1.6; }}
        .warning {{ color: #dc2626; font-size: 14px; margin-top: 30px; padding: 15px; background: #fef2f2; border-radius: 6px; }}
        .footer {{ text-align: center; margin-top: 30px; color: #9ca3af; font-size: 14px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""card"">
            <div class=""header"">
                <h1>Email Verification</h1>
            </div>
            <p class=""message"">Hello {WebUtility.HtmlEncode(recipientName)},</p>
            <p class=""message"">Please use the following verification code to confirm your email address:</p>
            <div class=""otp-container"">
                <div class=""otp-code"">{otp}</div>
            </div>
            <p class=""message"">This code will expire in <strong>{expirationMinutes} minutes</strong>.</p>
            <div class=""warning"">
                <strong>Security Notice:</strong> If you did not request this verification code, please ignore this email. Do not share this code with anyone.
            </div>
        </div>
        <div class=""footer"">
            <p>This is an automated message from {WebUtility.HtmlEncode(_settings.SenderName)}.</p>
            <p>Please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>";

        var textBody = $@"
Email Verification

Hello {recipientName},

Please use the following verification code to confirm your email address:

{otp}

This code will expire in {expirationMinutes} minutes.

Security Notice: If you did not request this verification code, please ignore this email. Do not share this code with anyone.

---
This is an automated message from {_settings.SenderName}.
Please do not reply to this email.";

        return await SendAsync(toEmail, subject, htmlBody, textBody, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation(
                "Email sending disabled. Would have sent to {Email}: {Subject}",
                toEmail, subject);
            return true;
        }

        try
        {
            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrEmpty(_settings.Username))
            {
                client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                IsBodyHtml = true,
                Body = htmlBody
            };
            message.To.Add(toEmail);

            if (!string.IsNullOrEmpty(textBody))
            {
                var plainView = AlternateView.CreateAlternateViewFromString(textBody, null, "text/plain");
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html");
                message.AlternateViews.Add(plainView);
                message.AlternateViews.Add(htmlView);
            }

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation("Email sent successfully to {Email}: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}: {Subject}", toEmail, subject);
            return false;
        }
    }
}
