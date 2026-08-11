using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// Builds a real <see cref="SecretOperationChallengeService"/> over mocked
/// dependencies, plus the arrange helpers the secret-operation tests share.
/// </summary>
/// <remarks>
/// The service is concrete by design (it is a plain application service, like
/// DeletionOtpService), so the tests exercise the real decision logic and mock
/// only the edges. That is the point: the guard these tests protect is the
/// service's own reasoning about who may spend which approval on what.
/// </remarks>
public sealed class SecretChallengeTestContext
{
    public const string CorrectCode = "123456";
    public const string CodeHash = "hash-of-123456";

    public Mock<ISecretOperationChallengeRepository> ChallengeRepository { get; } = new();
    public Mock<IUserRepository> UserRepository { get; } = new();
    public Mock<INotificationService> NotificationService { get; } = new();
    public Mock<IOtpGenerator> OtpGenerator { get; } = new();
    public Mock<IPasswordHasher> PasswordHasher { get; } = new();
    public Mock<IEnvironmentInfo> EnvironmentInfo { get; } = new();

    public EmailSettings EmailSettings { get; } = new();

    public SecretOperationChallengeService Service { get; }

    public SecretChallengeTestContext()
    {
        OtpGenerator.Setup(g => g.GenerateNumericOtp(It.IsAny<int>())).Returns(CorrectCode);
        PasswordHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns(CodeHash);
        PasswordHasher
            .Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string code, string hash) => code == CorrectCode && hash == CodeHash);

        NotificationService
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        ChallengeRepository
            .Setup(r => r.MarkVerifiedAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ChallengeRepository
            .Setup(r => r.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Service = new SecretOperationChallengeService(
            ChallengeRepository.Object,
            UserRepository.Object,
            NotificationService.Object,
            OtpGenerator.Object,
            PasswordHasher.Object,
            TestHelpers.CreateOptions(EmailSettings),
            EnvironmentInfo.Object,
            new Mock<ILogger<SecretOperationChallengeService>>().Object);
    }

    /// <summary>Makes <paramref name="userId"/> resolve to a reachable administrator.</summary>
    public void WithConfirmedAdmin(Guid userId, string email = "admin@example.com")
    {
        UserRepository
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(userId, email, emailConfirmed: true));
    }

    /// <summary>Makes <paramref name="userId"/> resolve to an unreachable administrator.</summary>
    public void WithUnconfirmedAdmin(Guid userId, string email = "admin@example.com")
    {
        UserRepository
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(userId, email, emailConfirmed: false));
    }

    /// <summary>
    /// Puts a live, verified approval behind <paramref name="challengeId"/> and
    /// returns it.
    /// </summary>
    public SecretOperationChallenge WithApproval(
        Guid challengeId,
        SecretOperation operation,
        Guid requestedBy,
        string? payloadHash = null)
    {
        var now = DateTime.UtcNow;
        var challenge = new SecretOperationChallenge(
            challengeId,
            requestedBy,
            operation,
            payloadHash,
            CodeHash,
            expiresAt: now.AddMinutes(15),
            verifiedAt: now,
            approvalExpiresAt: now.AddMinutes(SecretOperationChallenge.ApprovalWindowMinutes),
            usedAt: null,
            attemptCount: 0,
            ipAddress: null,
            createdAt: now);

        ChallengeRepository
            .Setup(r => r.GetByIdAsync(challengeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        return challenge;
    }

    /// <summary>Puts an unanswered (unverified) challenge behind <paramref name="challengeId"/>.</summary>
    public SecretOperationChallenge WithOpenChallenge(
        Guid challengeId,
        SecretOperation operation,
        Guid requestedBy,
        int attemptCount = 0,
        DateTime? expiresAt = null)
    {
        var now = DateTime.UtcNow;
        var challenge = new SecretOperationChallenge(
            challengeId,
            requestedBy,
            operation,
            payloadHash: null,
            CodeHash,
            expiresAt: expiresAt ?? now.AddMinutes(15),
            verifiedAt: null,
            approvalExpiresAt: null,
            usedAt: null,
            attemptCount: attemptCount,
            ipAddress: null,
            createdAt: now);

        ChallengeRepository
            .Setup(r => r.GetByIdAsync(challengeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        // Model the conditional UPDATE rather than a bare bool: the slot is
        // claimed atomically and the count it tests is the stored one, so a
        // test can call VerifyAsync repeatedly and see the cap actually bite.
        var attemptsUsed = attemptCount;
        ChallengeRepository
            .Setup(r => r.TryRegisterAttemptAsync(challengeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int maxAttempts, CancellationToken _) =>
            {
                if (attemptsUsed >= maxAttempts)
                {
                    return false;
                }

                attemptsUsed++;
                return true;
            });

        return challenge;
    }

    private static User BuildUser(Guid id, string email, bool emailConfirmed)
    {
        var user = User.Create(
            email: email,
            passwordHash: "hash",
            firstName: "Platform",
            lastName: "Administrator",
            createdBy: id);

        // Create() always produces an unconfirmed account; the confirmed case is
        // reached through the same behaviour method production code uses.
        if (emailConfirmed)
        {
            user.ConfirmEmail(id);
        }

        return user;
    }
}
