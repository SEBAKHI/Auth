using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// <see cref="IPasswordResetLinkIssuer"/>: the reset link's whole life on the issuing side, moved
/// out of the forgot-password handler (S13, D7) so the two signed-in ways to ask for a link do not
/// grow copies of it.
/// </summary>
public sealed class PasswordResetLinkIssuer : IPasswordResetLinkIssuer
{
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ICredentialHasher _credentialHasher;
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly IEnvironmentInfo _environment;
    private readonly ILogger<PasswordResetLinkIssuer> _logger;

    public PasswordResetLinkIssuer(
        IPasswordResetTokenRepository passwordResetTokenRepository,
        ISecureTokenGenerator tokenGenerator,
        ICredentialHasher credentialHasher,
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IEnvironmentInfo environment,
        ILogger<PasswordResetLinkIssuer> logger)
    {
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _tokenGenerator = tokenGenerator;
        _credentialHasher = credentialHasher;
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PasswordResetLinkIssue> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var expirationMinutes = _emailSettings.ResetTokenExpirationMinutes;

        // One live link per account. If an unused, unexpired link already
        // exists it is still in the account owner's inbox, so nothing is
        // reissued: no new token, no invalidation, no mail. This is what stops
        // an anonymous caller from killing a victim's link by asking again —
        // before this check the call below invalidated every earlier link, so
        // a stranger firing this endpoint every few seconds kept the real
        // owner's link permanently dead and denied them recovery. The answer is
        // the same generic shape as a fresh issue, so a no-op cannot be told
        // apart from one (enumeration). The trade-off is explicit: an owner
        // whose mail was lost waits for the live link to expire
        // (Email:ResetTokenExpirationMinutes), which is therefore also the
        // reissue interval.
        if (await _passwordResetTokenRepository.HasLiveTokenAsync(user.Id, cancellationToken))
        {
            _logger.LogInformation(
                "Password reset requested for user {UserId} while a live reset link exists; not reissued",
                user.Id);

            return new PasswordResetLinkIssue(false, DateTime.UtcNow.AddMinutes(expirationMinutes));
        }

        // Only dead rows (expired, never used) can remain at this point; marking
        // them used is hygiene, and it no longer touches a link the owner may
        // still be holding.
        await _passwordResetTokenRepository.InvalidateAllForUserAsync(user.Id, cancellationToken);

        // Generate a new 256-bit token. It is hashed with HMAC-SHA256 rather than
        // Argon2id: the token is high-entropy so it cannot be guessed, and a
        // deterministic hash is what lets the token alone identify the row on
        // redemption - no email required. Mirrors RefreshTokens.
        var token = _tokenGenerator.Generate();
        var tokenHash = _credentialHasher.Hash(CredentialPurpose.PasswordReset, token);

        var resetToken = PasswordResetToken.Create(user.Id, tokenHash, expirationMinutes);
        await _passwordResetTokenRepository.CreateAsync(resetToken, cancellationToken);

        // With email disabled the log is the only other place the reset link
        // exists, which is what makes the flow testable locally. Gated on the
        // environment as well as the setting: this link IS the credential - it
        // resets the password with no further proof - and Email:Enabled is a hot
        // setting an operator can flip from the console in production, so on its
        // own it would put a live reset link in the production log. Note the URL
        // is not masked the way the address beside it is; Uri.EscapeDataString
        // is an encoding, not a redaction.
        if (!_emailSettings.Enabled && _environment.IsDevelopment)
        {
            _logger.LogWarning(
                "Email disabled - Password reset link for {Email}: {ResetUrl} (expires in {Minutes} minutes)",
                EmailMasking.Mask(user.Email), _emailSettings.BuildPasswordResetUrl(token), expirationMinutes);
        }

        // Send from the database-managed template; language follows the user's
        // stored PreferredLanguage. Link building stays a config concern here.
        var recipientName = user.NameToShow;
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = user.Email,
                RecipientName = recipientName,
                RecipientUserId = user.Id,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = recipientName,
                    ["ResetLink"] = _emailSettings.BuildPasswordResetUrl(token),
                    ["ExpirationMinutes"] = expirationMinutes
                }
            },
            cancellationToken);

        // The answer stays a generic success even when the email could not be
        // delivered; the plaintext token is never returned to a caller.
        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send password reset email to user {UserId}: {Error}",
                user.Id, sendResult.FirstError.Description);
        }

        _logger.LogInformation("Password reset token generated for user {UserId}", user.Id);

        return new PasswordResetLinkIssue(true, resetToken.ExpiresAt);
    }
}
