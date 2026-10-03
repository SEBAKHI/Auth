using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Issues and checks the code an account enters before it binds its FIRST second
/// factor, emailed to its confirmed address (<see cref="FirstFactorEmailProofPolicy"/>
/// says when it is needed).
/// </summary>
/// <remarks>
/// Factor-agnostic: it knows nothing of authenticator apps. The caller checks its
/// own factor first, reserves an attempt on the code here, and spends the code in
/// the same transaction that binds the factor — switching TOTP on spends it in
/// <see cref="ITwoFactorStateStore.TryEnableAsync"/>.
/// <para>
/// The code proves the mailbox for that bind and nothing else: it is never a second
/// factor, and nothing here issues a token or touches a session. It is hashed with a
/// purpose label in its scope, so its stored form is unrelated to any other code of
/// the same account, and it is never returned or logged.
/// </para>
/// </remarks>
public class FirstFactorEmailProof
{
    /// <summary>
    /// The purpose label the code is hashed under, before the account id. Other
    /// codes of the account are hashed under the bare id, so a stored value of one
    /// can never verify as the other.
    /// </summary>
    private const string ScopePrefix = "two-factor-bind:";

    private const int CodeDigits = 6;

    private readonly ITwoFactorBindCodeRepository _codeRepository;
    private readonly IUserRepository _userRepository;
    private readonly INotificationService _notificationService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IOtpHasher _otpHasher;
    private readonly EmailSettings _emailSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FirstFactorEmailProof> _logger;

    public FirstFactorEmailProof(
        ITwoFactorBindCodeRepository codeRepository,
        IUserRepository userRepository,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IOtpHasher otpHasher,
        IOptionsSnapshot<EmailSettings> emailSettings,
        TimeProvider timeProvider,
        ILogger<FirstFactorEmailProof> logger)
    {
        _codeRepository = codeRepository;
        _userRepository = userRepository;
        _notificationService = notificationService;
        _otpGenerator = otpGenerator;
        _otpHasher = otpHasher;
        _emailSettings = emailSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// The scope a code of <paramref name="userId"/> is hashed and verified under.
    /// </summary>
    public static string HashScope(Guid userId) => ScopePrefix + userId;

    /// <summary>
    /// Issues a fresh code and emails it to the account's confirmed address. Every
    /// code still outstanding is superseded first, so a guesser never has more than
    /// one live target.
    /// </summary>
    /// <param name="userId">The account binding its first factor.</param>
    /// <param name="deviceName">The browser and operating system the request came from, when known.</param>
    /// <param name="ipAddress">The requesting client address, for the email and the audit column.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The masked address the code went to, and when it expires.</returns>
    public async Task<ErrorOr<FirstFactorEmailCodeSent>> SendAsync(
        Guid userId,
        string? deviceName,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Only an address the account proved may receive a code that binds a
        // factor to it; anything else would move the proof to a stranger's mailbox.
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email.Value))
        {
            _logger.LogWarning(
                "First-factor email code refused: user {UserId} has no confirmed email to receive it",
                userId);
            return TwoFactorErrors.EmailCodeRecipientUnavailable;
        }

        // The per-account cap is what guards the mailbox: no number of client
        // addresses gets around it.
        var recentCount = await _codeRepository.GetRecentCountForUserAsync(
            userId, TimeSpan.FromSeconds(_emailSettings.RateLimitWindowSeconds), cancellationToken);
        if (recentCount >= _emailSettings.MaxOtpRequestsPerWindow)
        {
            _logger.LogWarning(
                "Rate limit exceeded for first-factor email codes for user {UserId}",
                userId);
            return TwoFactorErrors.EmailCodeTooManyRequests;
        }

        await _codeRepository.InvalidateOutstandingForUserAsync(userId, cancellationToken);

        var otp = _otpGenerator.GenerateNumericOtp(CodeDigits);
        var code = TwoFactorBindCode.Issue(
            userId,
            _otpHasher.Hash(HashScope(userId), otp),
            ipAddress,
            _timeProvider.GetUtcNow().UtcDateTime,
            _emailSettings.OtpExpirationMinutes);

        await _codeRepository.CreateAsync(code, cancellationToken);

        // Sent while email is on — the only state in which a code is required — so
        // there is no development log line that prints it: the code exists in the
        // email alone.
        var recipientName = user.DisplayName ?? user.GetFullName();
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.TwoFactorBindCode,
                RecipientAddress = user.Email.Value,
                RecipientName = recipientName,
                RecipientUserId = user.Id,
                // The platform's own security message: its global templates,
                // whichever application the request came from.
                ApplicationId = null,
                TriggeredBy = user.Id,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = recipientName,
                    ["OtpCode"] = otp,
                    ["ExpirationMinutes"] = _emailSettings.OtpExpirationMinutes,
                    ["RequestedAt"] = code.CreatedAt.ToString("u", CultureInfo.InvariantCulture),
                    ["DeviceName"] = deviceName,
                    ["IpAddress"] = ipAddress
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send the first-factor email code to user {UserId}: {Error}",
                userId, sendResult.FirstError.Description);
            return TwoFactorErrors.EmailCodeSendFailed;
        }

        _logger.LogInformation(
            "First-factor email code sent to user {UserId} from {IpAddress}",
            userId, ipAddress ?? "unknown");

        return new FirstFactorEmailCodeSent(EmailMasking.Mask(user.Email.Value), code.ExpiresAt);
    }

    /// <summary>
    /// Counts one attempt against the account's live code, then checks the
    /// submitted code against it. The attempt stays counted whatever the outcome;
    /// the caller spends the code in the transaction that binds the factor.
    /// </summary>
    /// <param name="userId">The account binding its first factor.</param>
    /// <param name="submittedCode">The code the user typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The ID of the code to spend, or <see cref="TwoFactorErrors.EmailCodeInvalid"/>
    /// for every failure: no live code, out of attempts, or a wrong code.
    /// </returns>
    public async Task<ErrorOr<Guid>> ReserveAsync(
        Guid userId,
        string submittedCode,
        CancellationToken cancellationToken)
    {
        // Expired, superseded, spent and never-sent are one answer: telling them
        // apart would tell a guesser which assumption was right.
        var live = await _codeRepository.GetLiveForUserAsync(userId, cancellationToken);
        if (live is null)
        {
            return TwoFactorErrors.EmailCodeInvalid;
        }

        // The read above cannot decide for a burst of requests that all made it
        // together; the reservation counts and re-checks in one statement.
        if (await _codeRepository.TryReserveAttemptAsync(live.Id, TwoFactorBindCode.MaxAttempts, cancellationToken) is null)
        {
            return TwoFactorErrors.EmailCodeInvalid;
        }

        if (!_otpHasher.Verify(HashScope(userId), submittedCode, live.CodeHash))
        {
            _logger.LogWarning(
                "Rejected a first-factor email code for user {UserId}",
                userId);
            return TwoFactorErrors.EmailCodeInvalid;
        }

        return live.Id;
    }
}

/// <summary>
/// A first-factor email code that was issued and handed to the notification
/// service.
/// </summary>
/// <param name="SentTo">The address it went to, masked.</param>
/// <param name="ExpiresAt">When it stops being accepted (UTC).</param>
public sealed record FirstFactorEmailCodeSent(string SentTo, DateTime ExpiresAt);
