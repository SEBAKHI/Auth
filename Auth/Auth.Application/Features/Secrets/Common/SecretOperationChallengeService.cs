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

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// Issues, verifies and spends the step-up confirmations that guard destructive
/// secret operations. One service behind all six operations, so the code path
/// that protects the gateway token is the same one that protects the key every
/// access token in the platform is signed with.
/// </summary>
/// <remarks>
/// Mirrors the deletion and ownership-transfer OTP semantics: Argon2id-hashed
/// six-digit code, short expiry, capped attempts, issuance rate limiting, and
/// one generic error for every verification failure shape.
/// </remarks>
public class SecretOperationChallengeService
{
    private readonly ISecretOperationChallengeRepository _challengeRepository;
    private readonly IUserRepository _userRepository;
    private readonly INotificationService _notificationService;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly EmailSettings _emailSettings;
    private readonly IEnvironmentInfo _environment;
    private readonly ILogger<SecretOperationChallengeService> _logger;

    public SecretOperationChallengeService(
        ISecretOperationChallengeRepository challengeRepository,
        IUserRepository userRepository,
        INotificationService notificationService,
        IOtpGenerator otpGenerator,
        IPasswordHasher passwordHasher,
        IOptionsSnapshot<EmailSettings> emailSettings,
        IEnvironmentInfo environment,
        ILogger<SecretOperationChallengeService> logger)
    {
        _challengeRepository = challengeRepository;
        _userRepository = userRepository;
        _notificationService = notificationService;
        _otpGenerator = otpGenerator;
        _passwordHasher = passwordHasher;
        _emailSettings = emailSettings.Value;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Generates a code, stores its hash bound to the operation, and emails it
    /// to the requesting administrator.
    /// </summary>
    /// <param name="operation">The operation the resulting approval will authorize.</param>
    /// <param name="payloadHash">Digest of the key material for imports; null for generates.</param>
    /// <param name="requestedBy">The administrator requesting the operation.</param>
    /// <param name="ipAddress">The requesting client address, recorded for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ErrorOr<SecretOperationChallengeIssued>> IssueAsync(
        SecretOperation operation,
        string? payloadHash,
        Guid requestedBy,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Guid.Empty means the sub claim was absent or unparseable. An operation
        // this destructive is never run for an unidentified caller, and there is
        // no address to send a code to either.
        if (requestedBy == Guid.Empty)
        {
            return SecretErrors.ChallengeRecipientUnavailable;
        }

        var admin = await _userRepository.GetByIdAsync(requestedBy, cancellationToken);
        if (admin is null || !admin.EmailConfirmed || string.IsNullOrWhiteSpace(admin.Email.Value))
        {
            _logger.LogWarning(
                "Secret operation {Operation} refused: requester {UserId} has no confirmed email to receive a code",
                operation, requestedBy);
            return SecretErrors.ChallengeRecipientUnavailable;
        }

        var recentCount = await _challengeRepository.GetRecentCountForUserAsync(
            requestedBy, TimeSpan.FromSeconds(_emailSettings.RateLimitWindowSeconds), cancellationToken);
        if (recentCount >= _emailSettings.MaxOtpRequestsPerWindow)
        {
            _logger.LogWarning(
                "Rate limit exceeded for secret operation confirmation codes requested by {UserId}",
                requestedBy);
            return SecretErrors.TooManyChallengeRequests;
        }

        // A fresh code supersedes every outstanding one, so a guesser never has
        // more than one live target at a time.
        await _challengeRepository.InvalidateOutstandingForUserAsync(requestedBy, cancellationToken);

        var otp = _otpGenerator.GenerateNumericOtp(6);
        var codeHash = _passwordHasher.HashPassword(otp);

        var challenge = SecretOperationChallenge.Create(
            requestedBy,
            operation,
            payloadHash,
            codeHash,
            ipAddress,
            _emailSettings.OtpExpirationMinutes);

        await _challengeRepository.CreateAsync(challenge, cancellationToken);

        // With email disabled the log is the only other place the code exists,
        // which is what makes the flow testable locally. Gated on the
        // environment as well as the setting: this code authorizes rotating the
        // key every token in the platform is signed with, and Email:Enabled is a
        // hot setting an operator can flip from the console in production — on
        // its own it would put the code in plaintext in the production log.
        if (!_emailSettings.Enabled && _environment.IsDevelopment)
        {
            _logger.LogWarning(
                "Email disabled - Secret operation confirmation code for {Operation} (admin {Email}): {Otp} (expires in {Minutes} minutes)",
                operation, EmailMasking.Mask(admin.Email.Value), otp, _emailSettings.OtpExpirationMinutes);
        }

        var recipientName = !string.IsNullOrWhiteSpace(admin.DisplayName)
            ? admin.DisplayName
            : !string.IsNullOrWhiteSpace(admin.FirstName) ? admin.FirstName : "Administrator";

        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.SecretOperationChallenge,
                RecipientAddress = admin.Email.Value,
                RecipientName = recipientName,
                RecipientUserId = admin.Id,
                TriggeredBy = admin.Id,
                Variables = new Dictionary<string, object?>
                {
                    ["UserName"] = recipientName,
                    ["OperationCode"] = operation.ToString(),
                    ["OtpCode"] = otp,
                    ["ExpirationMinutes"] = _emailSettings.OtpExpirationMinutes,
                    ["IpAddress"] = ipAddress ?? "unknown",
                    ["RequestedAt"] = challenge.CreatedAt.ToString("u")
                }
            },
            cancellationToken);

        if (sendResult.IsError)
        {
            _logger.LogError(
                "Failed to send secret operation confirmation code for {Operation} to admin {UserId}: {Error}",
                operation, requestedBy, sendResult.FirstError.Description);
            return SecretErrors.ChallengeEmailFailed;
        }

        _logger.LogWarning(
            "Secret operation {Operation} confirmation requested by {UserId} from {IpAddress}; code sent",
            operation, requestedBy, ipAddress ?? "unknown");

        return new SecretOperationChallengeIssued(
            challenge.Id,
            challenge.ExpiresAt,
            EmailMasking.Mask(admin.Email.Value));
    }

    /// <summary>
    /// Checks a submitted code and, on success, opens the approval window.
    /// Every failure shape returns the same error.
    /// </summary>
    /// <param name="challengeId">The challenge being answered.</param>
    /// <param name="code">The six-digit code the administrator typed.</param>
    /// <param name="requestedBy">The administrator answering, for actor binding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ErrorOr<SecretOperationChallenge>> VerifyAsync(
        Guid challengeId,
        string code,
        Guid requestedBy,
        CancellationToken cancellationToken)
    {
        var challenge = await _challengeRepository.GetByIdAsync(challengeId, cancellationToken);

        // Unknown id and wrong owner are indistinguishable from a wrong code, on
        // purpose: an id is not a secret, and the response must not confirm that
        // a given challenge exists or whose it is.
        if (challenge is null || challenge.RequestedBy != requestedBy || !challenge.IsOpen)
        {
            return SecretErrors.InvalidChallengeCode;
        }

        // Claim an attempt BEFORE evaluating the code. The IsOpen check above is
        // only a fast path: it reads a count fetched earlier in this request, so
        // on its own it lets every request that arrives inside the Argon2id
        // verification window — which is deliberately slow — pass the same stale
        // count and spend a guess. This conditional update is the cap.
        var attemptClaimed = await _challengeRepository.TryRegisterAttemptAsync(
            challenge.Id, SecretOperationChallenge.MaxAttempts, cancellationToken);

        if (!attemptClaimed)
        {
            return SecretErrors.InvalidChallengeCode;
        }

        if (!_passwordHasher.VerifyPassword(code, challenge.CodeHash))
        {
            // Mirror the claimed attempt onto the in-memory row for the log line
            // only. It is deliberately NOT mirrored on the success path below,
            // where MarkVerified() re-checks IsOpen and would reject a correct
            // code that legitimately claimed the last attempt.
            challenge.IncrementAttempts();

            _logger.LogWarning(
                "Rejected secret operation confirmation code for {Operation} from {UserId} (attempt {Attempt} of {Max})",
                challenge.Operation, requestedBy, challenge.AttemptCount, SecretOperationChallenge.MaxAttempts);

            return SecretErrors.InvalidChallengeCode;
        }

        var verified = challenge.MarkVerified();
        if (verified.IsError)
        {
            return verified.Errors;
        }

        // The conditional update is what makes verification single-winner: two
        // concurrent correct codes cannot both open an approval window.
        var stored = await _challengeRepository.MarkVerifiedAsync(
            challenge.Id,
            challenge.VerifiedAt!.Value,
            challenge.ApprovalExpiresAt!.Value,
            SecretOperationChallenge.MaxAttempts,
            cancellationToken);

        if (!stored)
        {
            return SecretErrors.InvalidChallengeCode;
        }

        _logger.LogWarning(
            "Secret operation {Operation} confirmed by {UserId}; approval open until {ApprovalExpiresAt:u}",
            challenge.Operation, requestedBy, challenge.ApprovalExpiresAt);

        return challenge;
    }

    /// <summary>
    /// Spends an approval on the operation it was raised for. Called by every
    /// destructive handler as its first act, before any key material is touched.
    /// </summary>
    /// <param name="challengeId">The approval to spend.</param>
    /// <param name="operation">The operation about to execute.</param>
    /// <param name="payloadHash">Digest of the key material being submitted; null for generates.</param>
    /// <param name="requestedBy">The administrator executing the operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ErrorOr<Success>> ConsumeAsync(
        Guid challengeId,
        SecretOperation operation,
        string? payloadHash,
        Guid requestedBy,
        CancellationToken cancellationToken)
    {
        var challenge = await _challengeRepository.GetByIdAsync(challengeId, cancellationToken);
        if (challenge is null)
        {
            return SecretErrors.ChallengeNotApproved;
        }

        var spendable = challenge.EnsureSpendableFor(operation, payloadHash, requestedBy);
        if (spendable.IsError)
        {
            _logger.LogWarning(
                "Rejected secret operation {Operation} by {UserId}: approval {ChallengeId} is not spendable for it",
                operation, requestedBy, challengeId);
            return spendable.Errors;
        }

        // Single use is enforced here, in one conditional update, not by the
        // read above: two requests that both passed the check must not both
        // rotate a key.
        var consumed = await _challengeRepository.TryConsumeAsync(challenge.Id, cancellationToken);
        if (!consumed)
        {
            _logger.LogWarning(
                "Rejected secret operation {Operation} by {UserId}: approval {ChallengeId} was already spent",
                operation, requestedBy, challengeId);
            return SecretErrors.ChallengeNotApproved;
        }

        return Result.Success;
    }
}

/// <summary>
/// What the caller learns when a confirmation code is issued: which challenge to
/// answer, how long they have, and a masked hint of where the code went. Never
/// the code, and never the unmasked address.
/// </summary>
/// <param name="ChallengeId">The challenge to answer.</param>
/// <param name="ExpiresAt">UTC instant after which the code stops being accepted.</param>
/// <param name="MaskedEmail">Masked address the code was sent to.</param>
public record SecretOperationChallengeIssued(
    Guid ChallengeId,
    DateTime ExpiresAt,
    string MaskedEmail);
