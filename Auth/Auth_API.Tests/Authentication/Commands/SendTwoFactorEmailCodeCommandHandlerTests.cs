using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for SendTwoFactorEmailCodeCommandHandler: the email that carries the
/// code an account enters before it binds its first second factor.
/// </summary>
public class SendTwoFactorEmailCodeCommandHandlerTests
{
    private const string Otp = "482913";
    private const string ClientIp = "203.0.113.7";
    private const string DeviceName = "Firefox on Linux";

    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 14, 0, TimeSpan.Zero);

    private readonly Mock<IReauthenticationGuard> _guard = new();
    private readonly Mock<ITwoFactorStateStore> _stateStore = new();
    private readonly Mock<ITwoFactorBindCodeRepository> _codes = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IOtpGenerator> _otpGenerator = new();
    private readonly Mock<IOtpHasher> _otpHasher = new();
    private readonly Mock<ILogger<FirstFactorEmailProof>> _proofLogger = new();
    private readonly TwoFactorSettings _twoFactorSettings = new();
    private readonly EmailSettings _emailSettings = new()
    {
        Enabled = true,
        OtpExpirationMinutes = 15,
        RateLimitWindowSeconds = 60,
        MaxOtpRequestsPerWindow = 3
    };

    private SendTwoFactorEmailCodeCommandHandler CreateHandler(
        IReauthenticationGuard? guard = null,
        ITwoFactorStateStore? stateStore = null,
        ITwoFactorBindCodeRepository? codes = null,
        IUserRepository? users = null,
        INotificationService? notifications = null) =>
        new(
            guard ?? _guard.Object,
            new FirstFactorEmailProofPolicy(
                TestHelpers.CreateOptions(_twoFactorSettings),
                TestHelpers.CreateOptions(_emailSettings)),
            stateStore ?? _stateStore.Object,
            new FirstFactorEmailProof(
                codes ?? _codes.Object,
                users ?? _users.Object,
                notifications ?? _notifications.Object,
                _otpGenerator.Object,
                _otpHasher.Object,
                TestHelpers.CreateOptions(_emailSettings),
                new FixedTimeProvider(Now),
                _proofLogger.Object));

    private static SendTwoFactorEmailCodeCommand Command(Guid userId) => new(userId, SessionId, ClientIp);

    /// <summary>A recent session, a pending factor, a confirmed address, room under the cap, a working outbox.</summary>
    private User GivenEverythingInPlace(Guid userId)
    {
        _guard
            .Setup(g => g.EnsureRecentSignInAsync(userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, DeviceName));
        _stateStore
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(userId, "v2:pending", null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        var user = TestHelpers.CreateUser(id: userId, email: "owner@example.com", emailConfirmed: true);
        _users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _codes
            .Setup(r => r.GetRecentCountForUserAsync(userId, TimeSpan.FromSeconds(60), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _otpGenerator.Setup(g => g.GenerateNumericOtp(6)).Returns(Otp);
        _otpHasher
            .Setup(h => h.Hash(It.IsAny<string>(), Otp))
            .Returns<string, string>((scope, _) => $"hash[{scope}]");
        _notifications
            .Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        return user;
    }

    private void VerifyNothingIssued()
    {
        _codes.Verify(r => r.InvalidateOutstandingForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _codes.Verify(r => r.CreateAsync(It.IsAny<TwoFactorBindCode>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StaleSession_AsksToSignInAgain_BeforeAnything()
    {
        // Every other dependency is strict with nothing set up: any read, mint or
        // send past the refused check fails the test.
        var userId = Guid.NewGuid();
        var guard = new Mock<IReauthenticationGuard>(MockBehavior.Strict);
        guard
            .Setup(g => g.EnsureRecentSignInAsync(userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);

        var result = await CreateHandler(
                guard: guard.Object,
                stateStore: new Mock<ITwoFactorStateStore>(MockBehavior.Strict).Object,
                codes: new Mock<ITwoFactorBindCodeRepository>(MockBehavior.Strict).Object,
                users: new Mock<IUserRepository>(MockBehavior.Strict).Object,
                notifications: new Mock<INotificationService>(MockBehavior.Strict).Object)
            .Handle(Command(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Handle_NotRequired_AnswersFalse_WithNoCodeAndNoEmail(bool emailEnabled, bool switchOn)
    {
        _emailSettings.Enabled = emailEnabled;
        _twoFactorSettings.RequireEmailCodeForFirstFactor = switchOn;
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().Be(new TwoFactorEmailCodeResponse(false, null, null));
        _stateStore.Verify(s => s.GetSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingIssued();
    }

    [Fact]
    public async Task Handle_NoPendingFactor_ReturnsSetupRequired_WithoutMinting()
    {
        // Codes are minted only while a first factor is being set up.
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        _stateStore.Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((TwoFactorSnapshot?)null);

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.SetupRequired.Code);
        VerifyNothingIssued();
    }

    [Fact]
    public async Task Handle_FactorAlreadyEnabled_ReturnsAlreadyEnabled_WithoutMinting()
    {
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        _stateStore
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(userId, "v2:on", "[]", isEnabled: true, failedAttempts: 0, lockedUntil: null));

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorAlreadyEnabled.Code);
        VerifyNothingIssued();
    }

    [Fact]
    public async Task Handle_NoConfirmedAddress_ReturnsRecipientUnavailable_WithoutMinting()
    {
        // A code that binds a factor goes only to an address the account proved.
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        _users
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId, email: "owner@example.com", emailConfirmed: false));

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeRecipientUnavailable.Code);
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        VerifyNothingIssued();
    }

    [Fact]
    public async Task Handle_CapReached_ReturnsTooManyRequests_WithNoRowAndNoEmail()
    {
        // The per-account cap is what guards the mailbox: no number of client
        // addresses gets around it.
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        _codes
            .Setup(r => r.GetRecentCountForUserAsync(userId, TimeSpan.FromSeconds(60), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeTooManyRequests.Code);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        VerifyNothingIssued();
    }

    [Fact]
    public async Task Handle_Sends_SupersedingOutstandingCodesBeforeTheNewOne()
    {
        // A guesser never has more than one live target.
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        var order = new List<string>();
        _codes
            .Setup(r => r.InvalidateOutstandingForUserAsync(userId, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("supersede"))
            .Returns(Task.CompletedTask);
        _codes
            .Setup(r => r.CreateAsync(It.IsAny<TwoFactorBindCode>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("insert"))
            .Returns(Task.CompletedTask);
        _notifications
            .Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("send"))
            .ReturnsAsync(Result.Success);

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        order.Should().Equal("supersede", "insert", "send");
    }

    [Fact]
    public async Task Handle_HashesTheCodeUnderThePurposeLabel_AndStoresOnlyTheHash()
    {
        // The purpose label comes first: every other code of the account is hashed
        // under the bare id, so a stored value of one can never verify as the other.
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        TwoFactorBindCode? stored = null;
        _codes
            .Setup(r => r.CreateAsync(It.IsAny<TwoFactorBindCode>(), It.IsAny<CancellationToken>()))
            .Callback<TwoFactorBindCode, CancellationToken>((code, _) => stored = code)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(Command(userId), CancellationToken.None);

        _otpHasher.Verify(h => h.Hash($"two-factor-bind:{userId}", Otp), Times.Once);
        FirstFactorEmailProof.HashScope(userId).Should().StartWith("two-factor-bind:").And.NotBe(userId.ToString());
        stored.Should().NotBeNull();
        stored!.CodeHash.Should().Be($"hash[two-factor-bind:{userId}]");
        stored.CodeHash.Should().NotContain(Otp);
        stored.UserId.Should().Be(userId);
        stored.IpAddress.Should().Be(ClientIp);
        stored.CreatedAt.Should().Be(Now.UtcDateTime);
        stored.ExpiresAt.Should().Be(Now.UtcDateTime.AddMinutes(15));
        stored.AttemptCount.Should().Be(0);
        stored.UsedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Sends_TheBindCodeEmail_AndAnswersWithTheMaskedAddressAndExpiry()
    {
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        NotificationRequest? sent = null;
        _notifications
            .Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(Result.Success);

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.EmailCodeRequired.Should().BeTrue();
        result.Value.SentTo.Should().Be("o***r@example.com", "the address the code went to, masked");
        result.Value.ExpiresAt.Should().Be(Now.UtcDateTime.AddMinutes(15));
        result.Value.ToString().Should().NotContain(Otp, "the code is never returned");

        sent.Should().NotBeNull();
        sent!.TypeCode.Should().Be(NotificationTypeCodes.TwoFactorBindCode);
        sent.RecipientAddress.Should().Be("owner@example.com");
        sent.RecipientUserId.Should().Be(userId, "the email is written in the account's own language");
        sent.ApplicationId.Should().BeNull("the platform's own security message uses its global templates");
        sent.Variables["OtpCode"].Should().Be(Otp);
        sent.Variables["ExpirationMinutes"].Should().Be(15);
        sent.Variables["RequestedAt"].Should().Be("2026-10-03 09:14:00Z");
        sent.Variables["DeviceName"].Should().Be(DeviceName);
        sent.Variables["IpAddress"].Should().Be(ClientIp);
        sent.Variables.Keys.Should().BeEquivalentTo(
            ["UserName", "OtpCode", "ExpirationMinutes", "RequestedAt", "DeviceName", "IpAddress"],
            "the variables the seeded catalog declares, and no other");
    }

    [Fact]
    public async Task Handle_SendFails_ReturnsSendFailed()
    {
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);
        _notifications
            .Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationErrors.SendFailed);

        var result = await CreateHandler().Handle(Command(userId), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeSendFailed.Code);
        result.FirstError.Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public async Task Handle_NeverLogsTheCode()
    {
        // The code exists in the email alone: no log line carries it, in any
        // environment (a code is required only while email is on).
        var userId = Guid.NewGuid();
        GivenEverythingInPlace(userId);

        await CreateHandler().Handle(Command(userId), CancellationToken.None);

        _proofLogger.Invocations.Should().NotBeEmpty("the send is logged, without the code");
        foreach (var invocation in _proofLogger.Invocations)
        {
            string.Join(" ", invocation.Arguments.Select(argument => argument?.ToString())).Should().NotContain(Otp);
        }
    }
}
