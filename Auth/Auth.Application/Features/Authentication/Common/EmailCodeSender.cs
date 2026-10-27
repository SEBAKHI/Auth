using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <inheritdoc cref="IEmailCodeSender"/>
/// <remarks>
/// The lines name the account, the challenge and the reason. They never carry the
/// code, the address, the client address or the user agent: the code exists in the
/// email alone, and the address in the account row.
/// </remarks>
public sealed class EmailCodeSender : IEmailCodeSender
{
    private const int CodeDigits = 6;

    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly INotificationService _notificationService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IOtpHasher _otpHasher;
    private readonly EmailSettings _emailSettings;
    private readonly TwoFactorSettings _twoFactorSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailCodeSender> _logger;

    public EmailCodeSender(
        ITwoFactorChallengeRepository challengeRepository,
        ITwoFactorStateStore twoFactorStateStore,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IOtpHasher otpHasher,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IOptionsSnapshot<TwoFactorSettings> twoFactorSettings,
        TimeProvider timeProvider,
        ILogger<EmailCodeSender> logger)
    {
        _challengeRepository = challengeRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _notificationService = notificationService;
        _otpGenerator = otpGenerator;
        _otpHasher = otpHasher;
        // Snapshots, read in this request's scope: both settings are hot, and a
        // value captured when the service was built would outlive a change.
        _emailSettings = emailSettings.Value;
        _twoFactorSettings = twoFactorSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<EmailCodeSent>> SendAsync(
        User user,
        TwoFactorChallenge challenge,
        string? ipAddress,
        int? hardLimit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(challenge);

        // Email off: the channel would mark the message sent with nothing sent, and
        // the person would wait for a code that never comes. Nothing is reserved.
        if (!_emailSettings.Enabled || string.IsNullOrWhiteSpace(user.Email.Value))
        {
            _logger.LogWarning(
                "{EventCode}: no code emailed for challenge {ChallengeId} of account {UserId}: {Reason}",
                TwoFactorSignalCodes.EmailCodeUnavailable, challenge.Id, user.Id,
                _emailSettings.Enabled ? "the account has no address" : "email sending is off");
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        var lifetimeMinutes = _twoFactorSettings.EffectiveEmailCodeLifetimeMinutes;
        var code = _otpGenerator.GenerateNumericOtp(CodeDigits);

        var receipt = await _challengeRepository.TryReserveEmailCodeSendAsync(
            new EmailCodeSendReservation(
                challenge.Id,
                challenge.Purpose,
                _otpHasher.Hash(EmailCodeProofStrategy.HashScope(challenge.Id), code),
                lifetimeMinutes,
                RequireBoundFactor: challenge.Purpose != TwoFactorChallengePurpose.EmailEnrollment,
                hardLimit,
                _emailSettings.RateLimitWindowSeconds,
                _emailSettings.MaxOtpRequestsPerWindow,
                TwoFactorAuth.MaxEmailCodesPerDay),
            cancellationToken);

        if (receipt is null)
        {
            return await RefusalAsync(user.Id, challenge, hardLimit, cancellationToken);
        }

        var recipientName = user.NameToShow;
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.TwoFactorEmailCode,
                RecipientAddress = user.Email.Value,
                RecipientName = recipientName,
                RecipientUserId = user.Id,
                // The platform's own security message: its global templates,
                // whichever application the sign-in came from.
                ApplicationId = null,
                TriggeredBy = user.Id,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = recipientName,
                    ["OtpCode"] = code,
                    ["ExpirationMinutes"] = lifetimeMinutes,
                    ["RequestedAt"] = _timeProvider.GetUtcNow().UtcDateTime.ToString("u", CultureInfo.InvariantCulture),
                    ["IpAddress"] = ipAddress,
                    // What the code is for (review of #107): a code to switch the
                    // emailed code on or off must not read as a sign-in code.
                    ["Purpose"] = TwoFactorEmailCodePurposes.Of(challenge.Purpose)
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            // The send was reserved and counts: the next request sends a new code.
            _logger.LogError(
                "Failed to queue the emailed second-factor code for challenge {ChallengeId} of account {UserId}: {Error}",
                challenge.Id, user.Id, sendResult.FirstError.Code);
            return TwoFactorErrors.EmailCodeSendFailed;
        }

        _logger.LogInformation(
            "Second-factor code {SendCount} of {MaxSends} emailed for challenge {ChallengeId} of account {UserId}",
            receipt.SendCount, TwoFactorChallenge.MaxEmailCodeSends, challenge.Id, user.Id);

        return new EmailCodeSent(
            receipt.ExpiresAt,
            EmailMasking.Mask(user.Email.Value),
            Math.Max(0, TwoFactorChallenge.MaxEmailCodeSends - receipt.SendCount));
    }

    /// <summary>
    /// Names a refused send once nothing is held, from the factor as it is now and
    /// the challenge as the caller read it. Every refusal is logged with its signal.
    /// </summary>
    private async Task<Error> RefusalAsync(
        Guid userId,
        TwoFactorChallenge challenge,
        int? hardLimit,
        CancellationToken cancellationToken)
    {
        if (!challenge.IsValid)
        {
            return TwoFactorErrors.ChallengeInvalid;
        }

        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(userId, cancellationToken);
        var needsFactor = challenge.Purpose != TwoFactorChallengePurpose.EmailEnrollment;

        if (needsFactor && snapshot?.HasEmailCode != true)
        {
            _logger.LogWarning(
                "{EventCode}: no code emailed for challenge {ChallengeId} of account {UserId}: {Reason}",
                TwoFactorSignalCodes.EmailCodeUnavailable, challenge.Id, userId, "the account's second factor is not the emailed code");
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        var (error, reason) = needsFactor && snapshot!.IsEmailCodeHardLocked(hardLimit)
            ? (TwoFactorErrors.EmailCodeLocked, "the emailed code reached the consecutive-failure limit")
            : needsFactor && snapshot!.IsEmailCodeLocked
                ? (TwoFactorErrors.LockedOut, "the emailed code is locked for a while")
                : (TwoFactorErrors.EmailCodeTooManyRequests, "a send limit was reached");

        _logger.LogWarning(
            "{EventCode}: no code emailed for challenge {ChallengeId} of account {UserId}: {Reason}",
            TwoFactorSignalCodes.EmailCodeThrottled, challenge.Id, userId, reason);

        return error;
    }
}
