using Auth.Application.Features.Secrets.Common;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// The step-up confirmation guarding every destructive secret operation. These
/// tests are the control: if any of them regress, an approval becomes
/// transferable between administrators, between operations, or between key
/// material — and the second factor stops being one.
/// </summary>
public class SecretOperationChallengeServiceTests
{
    private readonly SecretChallengeTestContext _context = new();
    private readonly Guid _admin = Guid.NewGuid();

    // ---- Issuing --------------------------------------------------------

    [Fact]
    public async Task Issue_SendsTheCodeToTheRequestingAdministratorOnly()
    {
        _context.WithConfirmedAdmin(_admin, "rotator@example.com");

        var result = await _context.Service.IssueAsync(
            SecretOperation.GenerateRsaKey, null, _admin, "203.0.113.7", CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.MaskedEmail.Should().NotContain("rotator@example.com",
            "the response must hint at the mailbox without disclosing the address");

        _context.NotificationService.Verify(
            s => s.SendAsync(
                It.Is<NotificationRequest>(r =>
                    r.TypeCode == NotificationTypeCodes.SecretOperationChallenge
                    && r.RecipientAddress == "rotator@example.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Issue_NamesTheOperationInTheEmail()
    {
        _context.WithConfirmedAdmin(_admin);

        await _context.Service.IssueAsync(
            SecretOperation.GenerateHmacKey, null, _admin, null, CancellationToken.None);

        _context.NotificationService.Verify(
            s => s.SendAsync(
                It.Is<NotificationRequest>(r =>
                    (string)r.Variables["OperationCode"]! == "GenerateHmacKey"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a recipient who cannot tell which key is being changed cannot tell " +
            "a request they made from one they did not");
    }

    [Fact]
    public async Task Issue_RefusesWhenTheAdministratorHasNoConfirmedAddress()
    {
        _context.WithUnconfirmedAdmin(_admin);

        var result = await _context.Service.IssueAsync(
            SecretOperation.GenerateRsaKey, null, _admin, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeRecipientUnavailable");
        _context.ChallengeRepository.Verify(
            r => r.CreateAsync(It.IsAny<SecretOperationChallenge>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a confirmation nobody can receive is not a confirmation");
    }

    [Fact]
    public async Task Issue_RefusesAnUnidentifiedCaller()
    {
        var result = await _context.Service.IssueAsync(
            SecretOperation.GenerateRsaKey, null, Guid.Empty, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeRecipientUnavailable");
        _context.UserRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Issue_RateLimitsPerAdministrator()
    {
        _context.WithConfirmedAdmin(_admin);
        _context.ChallengeRepository
            .Setup(r => r.GetRecentCountForUserAsync(
                _admin, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_context.EmailSettings.MaxOtpRequestsPerWindow);

        var result = await _context.Service.IssueAsync(
            SecretOperation.GenerateRsaKey, null, _admin, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.TooManyChallengeRequests");
    }

    [Fact]
    public async Task Issue_SupersedesEveryOutstandingChallenge()
    {
        _context.WithConfirmedAdmin(_admin);

        await _context.Service.IssueAsync(
            SecretOperation.GenerateRsaKey, null, _admin, null, CancellationToken.None);

        _context.ChallengeRepository.Verify(
            r => r.InvalidateOutstandingForUserAsync(_admin, It.IsAny<CancellationToken>()),
            Times.Once,
            "a guesser must never accumulate more than one live target");
    }

    [Fact]
    public async Task Issue_FailsWhenTheEmailCannotBeSent()
    {
        _context.WithConfirmedAdmin(_admin);
        _context.NotificationService
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorOr.Error.Failure("Notification.Failed", "smtp down"));

        var result = await _context.Service.IssueAsync(
            SecretOperation.GenerateRsaKey, null, _admin, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeEmailFailed");
    }

    // ---- Verifying ------------------------------------------------------

    [Fact]
    public async Task Verify_AcceptsTheCorrectCodeAndOpensTheApprovalWindow()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(challengeId, SecretOperation.GenerateRsaKey, _admin);

        var result = await _context.Service.VerifyAsync(
            challengeId, SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApprovalExpiresAt.Should().NotBeNull();
        result.Value.ApprovalExpiresAt!.Value.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(SecretOperationChallenge.ApprovalWindowMinutes),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Verify_RejectsAWrongCodeAndCountsTheAttempt()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(challengeId, SecretOperation.GenerateRsaKey, _admin);

        var result = await _context.Service.VerifyAsync(
            challengeId, "000000", _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidChallengeCode");
        _context.ChallengeRepository.Verify(
            r => r.TryRegisterAttemptAsync(
                challengeId, SecretOperationChallenge.MaxAttempts, It.IsAny<CancellationToken>()),
            Times.Once,
            "the attempt must be claimed before the caller is answered, or " +
            "abandoning the request resets the cap");
    }

    [Fact]
    public async Task Verify_ClaimsTheAttemptBeforeEvaluatingTheCode()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(challengeId, SecretOperation.GenerateRsaKey, _admin);

        await _context.Service.VerifyAsync(
            challengeId, SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        // Reserving only on the failure path would leave the cap advisory: every
        // request that arrived during the (deliberately slow) hash comparison
        // would have read the same count and been handed a guess.
        _context.ChallengeRepository.Verify(
            r => r.TryRegisterAttemptAsync(
                challengeId, SecretOperationChallenge.MaxAttempts, It.IsAny<CancellationToken>()),
            Times.Once,
            "a correct code must consume an attempt slot too, because the slot " +
            "is claimed before the code is known to be correct");
    }

    [Fact]
    public async Task Verify_RefusesTheCodeWhenTheStoreWillNotGrantAnAttempt()
    {
        var challengeId = Guid.NewGuid();

        // The row this request read still looks open — attempts are at zero —
        // but concurrent requests exhausted the cap before this one reached the
        // store. That gap is precisely what the in-memory check cannot see, so
        // the store's answer, not the snapshot, has to decide.
        _context.WithOpenChallenge(challengeId, SecretOperation.GenerateRsaKey, _admin);
        _context.ChallengeRepository
            .Setup(r => r.TryRegisterAttemptAsync(
                challengeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _context.Service.VerifyAsync(
            challengeId, SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidChallengeCode");
        _context.PasswordHasher.Verify(
            h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "a request that cannot claim an attempt must be answered without " +
            "evaluating the submitted code");
        _context.ChallengeRepository.Verify(
            r => r.MarkVerifiedAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "and it must never open an approval window");
    }

    [Fact]
    public async Task Verify_RejectsTheCorrectCodeOnceAttemptsAreExhausted()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(
            challengeId, SecretOperation.GenerateRsaKey, _admin,
            attemptCount: SecretOperationChallenge.MaxAttempts);

        var result = await _context.Service.VerifyAsync(
            challengeId, SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidChallengeCode");
    }

    [Fact]
    public async Task Verify_RejectsTheCorrectCodeAfterTheWindowClosed()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(
            challengeId, SecretOperation.GenerateRsaKey, _admin,
            expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var result = await _context.Service.VerifyAsync(
            challengeId, SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidChallengeCode");
    }

    [Fact]
    public async Task Verify_RejectsAnotherAdministratorsChallenge()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(challengeId, SecretOperation.GenerateRsaKey, Guid.NewGuid());

        var result = await _context.Service.VerifyAsync(
            challengeId, SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidChallengeCode",
            "a wrong owner must be indistinguishable from a wrong code");
    }

    [Fact]
    public async Task Verify_RejectsAnUnknownChallengeWithTheSameError()
    {
        var result = await _context.Service.VerifyAsync(
            Guid.NewGuid(), SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidChallengeCode");
    }

    [Fact]
    public async Task Verify_LosesTheRaceWhenAnotherRequestVerifiedFirst()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(challengeId, SecretOperation.GenerateRsaKey, _admin);
        _context.ChallengeRepository
            .Setup(r => r.MarkVerifiedAsync(
                challengeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _context.Service.VerifyAsync(
            challengeId, SecretChallengeTestContext.CorrectCode, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidChallengeCode");
    }

    // ---- Spending -------------------------------------------------------

    [Fact]
    public async Task Consume_SpendsAMatchingApproval()
    {
        var challengeId = Guid.NewGuid();
        _context.WithApproval(challengeId, SecretOperation.GenerateHmacKey, _admin);

        var result = await _context.Service.ConsumeAsync(
            challengeId, SecretOperation.GenerateHmacKey, null, _admin, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _context.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(challengeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_RefusesAnApprovalRaisedForADifferentOperation()
    {
        var challengeId = Guid.NewGuid();
        _context.WithApproval(challengeId, SecretOperation.GenerateGatewayToken, _admin);

        var result = await _context.Service.ConsumeAsync(
            challengeId, SecretOperation.GenerateHmacKey, null, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _context.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "confirming the least destructive operation must never authorize the most");
    }

    [Fact]
    public async Task Consume_RefusesAnApprovalRaisedByADifferentAdministrator()
    {
        var challengeId = Guid.NewGuid();
        _context.WithApproval(challengeId, SecretOperation.GenerateHmacKey, Guid.NewGuid());

        var result = await _context.Service.ConsumeAsync(
            challengeId, SecretOperation.GenerateHmacKey, null, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
    }

    [Fact]
    public async Task Consume_RefusesKeyMaterialThatWasNotTheMaterialApproved()
    {
        var challengeId = Guid.NewGuid();
        var approved = SecretPayloadDigest.Compute("-----BEGIN PRIVATE KEY----- approved");
        _context.WithApproval(challengeId, SecretOperation.ImportRsaKey, _admin, approved);

        var result = await _context.Service.ConsumeAsync(
            challengeId,
            SecretOperation.ImportRsaKey,
            SecretPayloadDigest.Compute("-----BEGIN PRIVATE KEY----- swapped"),
            _admin,
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _context.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an approval obtained for one key must not install another");
    }

    [Fact]
    public async Task Consume_RefusesAnUnverifiedChallenge()
    {
        var challengeId = Guid.NewGuid();
        _context.WithOpenChallenge(challengeId, SecretOperation.GenerateHmacKey, _admin);

        var result = await _context.Service.ConsumeAsync(
            challengeId, SecretOperation.GenerateHmacKey, null, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
    }

    [Fact]
    public async Task Consume_RefusesWhenTheApprovalWasAlreadySpent()
    {
        var challengeId = Guid.NewGuid();
        _context.WithApproval(challengeId, SecretOperation.GenerateHmacKey, _admin);
        _context.ChallengeRepository
            .Setup(r => r.TryConsumeAsync(challengeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _context.Service.ConsumeAsync(
            challengeId, SecretOperation.GenerateHmacKey, null, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved",
            "single use is decided by the conditional update, not by the read before it");
    }

    [Fact]
    public async Task Consume_RefusesAnUnknownApproval()
    {
        var result = await _context.Service.ConsumeAsync(
            Guid.NewGuid(), SecretOperation.GenerateHmacKey, null, _admin, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
    }
}
