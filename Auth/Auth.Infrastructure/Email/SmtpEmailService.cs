using System.Globalization;
using System.Net;
using Auth_Localization.Resources.Email;
using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Auth.Infrastructure.Email;

/// <summary>
/// SMTP implementation of the email service with localized templates.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private const int SendTimeoutMilliseconds = 30_000;

    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly IStringLocalizer<EmailTemplates> _localizer;

    private static readonly string[] RtlCultures = ["ar", "fa", "ur", "he"];

    public SmtpEmailService(
        IOptions<EmailSettings> settings,
        ILogger<SmtpEmailService> logger,
        IStringLocalizer<EmailTemplates> localizer)
    {
        _settings = settings.Value;
        _logger = logger;
        _localizer = localizer;
    }

    /// <inheritdoc />
    public async Task<bool> SendVerificationOtpAsync(
        string toEmail,
        string recipientName,
        string otp,
        int expirationMinutes,
        CancellationToken cancellationToken)
    {
        var subject = Localize("Email.Verification.Subject", "Verify Your Email Address");
        var heading = Localize("Email.Verification.Heading", "Email Verification");
        var greeting = LocalizeFormat("Email.Verification.Greeting", "Hello {0},", recipientName);
        var instruction = Localize("Email.Verification.Instruction",
            "Please use the following verification code to confirm your email address:");
        var expiration = LocalizeFormat("Email.Verification.Expiration",
            "This code will expire in {0} minutes.", expirationMinutes);
        var securityNotice = Localize("Email.Verification.SecurityNotice",
            "Security Notice: If you did not request this verification code, please ignore this email. Do not share this code with anyone.");
        var footer = LocalizeFormat("Email.Verification.Footer",
            "This is an automated message from {0}. Please do not reply to this email.", _settings.SenderName);

        var contentHtml = $@"
            <p class=""message"">{WebUtility.HtmlEncode(greeting)}</p>
            <p class=""message"">{WebUtility.HtmlEncode(instruction)}</p>
            <div class=""code-container"">
                <div class=""otp-code"">{otp}</div>
            </div>
            <p class=""message"">{WebUtility.HtmlEncode(expiration)}</p>
            <div class=""warning"">
                {WebUtility.HtmlEncode(securityNotice)}
            </div>";

        var htmlBody = BuildHtmlDocument(heading, contentHtml, footer);

        var textBody = $@"
{heading}

{greeting}

{instruction}

{otp}

{expiration}

{securityNotice}

---
{footer}";

        return await SendAsync(toEmail, subject, htmlBody, textBody, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendPasswordResetAsync(
        string toEmail,
        string recipientName,
        string resetToken,
        int expirationMinutes,
        CancellationToken cancellationToken)
    {
        var subject = Localize("Email.PasswordReset.Subject", "Reset Your Password");
        var heading = Localize("Email.PasswordReset.Heading", "Password Reset");
        var greeting = LocalizeFormat("Email.PasswordReset.Greeting", "Hello {0},", recipientName);
        var instruction = Localize("Email.PasswordReset.Instruction",
            "We received a request to reset your password. Click the button below to choose a new password:");
        var buttonText = Localize("Email.PasswordReset.ButtonText", "Reset Password");
        var tokenInstruction = Localize("Email.PasswordReset.TokenInstruction",
            "Or enter this code on the password reset page:");
        var expiration = LocalizeFormat("Email.PasswordReset.Expiration",
            "This link will expire in {0} minutes.", expirationMinutes);
        var securityNotice = Localize("Email.PasswordReset.SecurityNotice",
            "Security Notice: If you did not request a password reset, please ignore this email. Your password will remain unchanged.");
        var linkFallback = Localize("Email.Common.LinkFallback",
            "If the button does not work, copy and paste this link into your browser:");
        var footer = LocalizeFormat("Email.Common.Footer",
            "This is an automated message from {0}. Please do not reply to this email.", _settings.SenderName);

        var resetUrl = BuildFrontendUrl($"/reset-password?token={Uri.EscapeDataString(resetToken)}");
        var encodedUrl = WebUtility.HtmlEncode(resetUrl);

        var contentHtml = $@"
            <p class=""message"">{WebUtility.HtmlEncode(greeting)}</p>
            <p class=""message"">{WebUtility.HtmlEncode(instruction)}</p>
            <div class=""button-container"">
                <a class=""button"" href=""{encodedUrl}"">{WebUtility.HtmlEncode(buttonText)}</a>
            </div>
            <p class=""link-fallback"">{WebUtility.HtmlEncode(linkFallback)}<br><a href=""{encodedUrl}"">{encodedUrl}</a></p>
            <p class=""message"">{WebUtility.HtmlEncode(tokenInstruction)}</p>
            <div class=""code-container"">
                <div class=""token-code"">{WebUtility.HtmlEncode(resetToken)}</div>
            </div>
            <p class=""message"">{WebUtility.HtmlEncode(expiration)}</p>
            <div class=""warning"">
                {WebUtility.HtmlEncode(securityNotice)}
            </div>";

        var htmlBody = BuildHtmlDocument(heading, contentHtml, footer);

        var textBody = $@"
{heading}

{greeting}

{instruction}

{resetUrl}

{tokenInstruction}

{resetToken}

{expiration}

{securityNotice}

---
{footer}";

        return await SendAsync(toEmail, subject, htmlBody, textBody, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendInvitationAsync(
        string toEmail,
        string organizationName,
        string inviterName,
        string invitationToken,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        var subject = LocalizeFormat("Email.Invitation.Subject",
            "You're Invited to Join {0}", organizationName);
        var heading = Localize("Email.Invitation.Heading", "Organization Invitation");
        var greeting = Localize("Email.Invitation.Greeting", "Hello,");
        var instruction = LocalizeFormat("Email.Invitation.Instruction",
            "{0} has invited you to join {1}.", inviterName, organizationName);
        var tokenInstruction = Localize("Email.Invitation.TokenInstruction",
            "Or enter this invitation code on the invitation page:");
        var buttonText = Localize("Email.Invitation.ButtonText", "Accept Invitation");
        var expiration = LocalizeFormat("Email.Invitation.Expiration",
            "This invitation expires on {0}.",
            expiresAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        var securityNotice = Localize("Email.Invitation.SecurityNotice",
            "Security Notice: If you were not expecting this invitation, please ignore this email.");
        var linkFallback = Localize("Email.Common.LinkFallback",
            "If the button does not work, copy and paste this link into your browser:");
        var footer = LocalizeFormat("Email.Common.Footer",
            "This is an automated message from {0}. Please do not reply to this email.", _settings.SenderName);

        var invitationUrl = BuildFrontendUrl($"/accept-invitation?token={Uri.EscapeDataString(invitationToken)}");
        var encodedUrl = WebUtility.HtmlEncode(invitationUrl);

        var contentHtml = $@"
            <p class=""message"">{WebUtility.HtmlEncode(greeting)}</p>
            <p class=""message"">{WebUtility.HtmlEncode(instruction)}</p>
            <div class=""button-container"">
                <a class=""button"" href=""{encodedUrl}"">{WebUtility.HtmlEncode(buttonText)}</a>
            </div>
            <p class=""link-fallback"">{WebUtility.HtmlEncode(linkFallback)}<br><a href=""{encodedUrl}"">{encodedUrl}</a></p>
            <p class=""message"">{WebUtility.HtmlEncode(tokenInstruction)}</p>
            <div class=""code-container"">
                <div class=""token-code"">{WebUtility.HtmlEncode(invitationToken)}</div>
            </div>
            <p class=""message"">{WebUtility.HtmlEncode(expiration)}</p>
            <div class=""warning"">
                {WebUtility.HtmlEncode(securityNotice)}
            </div>";

        var htmlBody = BuildHtmlDocument(heading, contentHtml, footer);

        var textBody = $@"
{heading}

{greeting}

{instruction}

{invitationUrl}

{tokenInstruction}

{invitationToken}

{expiration}

{securityNotice}

---
{footer}";

        return await SendAsync(toEmail, subject, htmlBody, textBody, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody,
        CancellationToken cancellationToken)
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
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            if (!string.IsNullOrEmpty(textBody))
            {
                bodyBuilder.TextBody = textBody;
            }
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient { Timeout = SendTimeoutMilliseconds };
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, GetSocketOptions(), cancellationToken);

            if (!string.IsNullOrEmpty(_settings.Username))
            {
                await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Email sent successfully to {Email}: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}: {Subject}", toEmail, subject);
            return false;
        }
    }

    /// <summary>
    /// Maps the configured port and SSL flag to the MailKit connection security mode.
    /// Port 465 uses implicit TLS (the server expects a TLS handshake immediately on connect);
    /// other ports use STARTTLS — required when <see cref="EmailSettings.UseSsl"/> is true,
    /// opportunistic otherwise (e.g. local development SMTP servers without TLS).
    /// </summary>
    private SecureSocketOptions GetSocketOptions() =>
        _settings.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : _settings.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;

    /// <summary>
    /// Wraps the given card content in the shared responsive, RTL-aware HTML email layout.
    /// </summary>
    private static string BuildHtmlDocument(string heading, string contentHtml, string footer)
    {
        var isRtl = IsRtlCulture();
        var dir = isRtl ? "rtl" : "ltr";
        var textAlign = isRtl ? "right" : "left";

        return $@"
<!DOCTYPE html>
<html dir=""{dir}"" lang=""{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; margin: 0; padding: 0; background-color: #f5f5f5; direction: {dir}; text-align: {textAlign}; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 40px 20px; }}
        .card {{ background: #ffffff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ text-align: center; margin-bottom: 30px; }}
        .header h1 {{ color: #1a1a1a; font-size: 24px; margin: 0; }}
        .code-container {{ text-align: center; margin: 30px 0; }}
        .otp-code {{ display: inline-block; font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #2563eb; padding: 20px 30px; background: #f3f4f6; border-radius: 8px; font-family: 'Courier New', monospace; direction: ltr; }}
        .token-code {{ display: inline-block; font-size: 16px; font-weight: bold; color: #2563eb; padding: 12px 20px; background: #f3f4f6; border-radius: 8px; font-family: 'Courier New', monospace; direction: ltr; word-break: break-all; }}
        .button-container {{ text-align: center; margin: 30px 0; }}
        .button {{ display: inline-block; background: #2563eb; color: #ffffff !important; font-size: 16px; font-weight: bold; text-decoration: none; padding: 14px 28px; border-radius: 6px; }}
        .link-fallback {{ color: #6b7280; font-size: 13px; word-break: break-all; }}
        .message {{ color: #4b5563; font-size: 16px; line-height: 1.6; }}
        .warning {{ color: #dc2626; font-size: 14px; margin-top: 30px; padding: 15px; background: #fef2f2; border-radius: 6px; }}
        .footer {{ text-align: center; margin-top: 30px; color: #9ca3af; font-size: 14px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""card"">
            <div class=""header"">
                <h1>{WebUtility.HtmlEncode(heading)}</h1>
            </div>
{contentHtml}
        </div>
        <div class=""footer"">
            <p>{WebUtility.HtmlEncode(footer)}</p>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Builds an absolute frontend URL from the configured <see cref="EmailSettings.FrontendBaseUrl"/>.
    /// </summary>
    private string BuildFrontendUrl(string pathAndQuery) =>
        $"{_settings.FrontendBaseUrl.TrimEnd('/')}{pathAndQuery}";

    private string Localize(string key, string fallback)
    {
        var localized = _localizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }

    private string LocalizeFormat(string key, string fallback, params object[] args)
    {
        var localized = _localizer[key];
        var template = localized.ResourceNotFound ? fallback : localized.Value;
        return string.Format(template, args);
    }

    private static bool IsRtlCulture()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return RtlCultures.Contains(culture);
    }
}
