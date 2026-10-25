using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.SendSignInEmailCode;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Notifications.Channels;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Emailing the code that completes a sign-in (S24, D-243, EO3, EO6): the resend after
/// the challenge's own send, after the password or a provider only, nothing while email
/// is off, every limit in one reservation, and the code nowhere but in the email.
/// </summary>
public class SignInEmailCodeSendTests
{
    private const string Code = "482913";

    private readonly User _user = TestHelpers.CreateUser(email: "owner@example.com", twoFactorEnabled: true);
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly CapturingLogger<EmailCodeSender> _logger = new();
    private readonly EmailSettings _email = new() { Enabled = true, MaxOtpRequestsPerWindow = 3, RateLimitWindowSeconds = 60 };
    private readonly TwoFactorSettings _twoFactor = new() { EmailCodeLifetimeMinutes = 7 };
    private EmailCodeSendReservation? _reserved;
    private NotificationRequest? _sent;

    public SignInEmailCodeSendTests()
    {
        _notifications.Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((request, _) => _sent = request)
            .ReturnsAsync(Result.Success);
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(EmailRow(_user.Id));
    }

    private void GivenReceipt(EmailCodeSendReceipt? receipt) =>
        _challenges.Setup(c => c.TryReserveEmailCodeSendAsync(It.IsAny<EmailCodeSendReservation>(), It.IsAny<CancellationToken>()))
            .Callback<EmailCodeSendReservation, CancellationToken>((reservation, _) => _reserved = reservation)
            .ReturnsAsync(receipt);

    private EmailCodeSender Sender()
    {
        var otp = new Mock<IOtpGenerator>();
        otp.Setup(o => o.GenerateNumericOtp(6)).Returns(Code);
        return new EmailCodeSender(
            _challenges.Object, _store.Object, _notifications.Object, otp.Object, ScopedOtpHasher.Instance,
            TestHelpers.CreateOptions(_email), TestHelpers.CreateOptions(_twoFactor), TimeProvider.System, _logger);
    }

    [Fact]
    public async Task ASentCode_IsReservedUnderEveryLimit_HashedForItsChallenge_AndOnlyTheEmailCarriesIt()
    {
        var challenge = ChallengeWith(_user.Id, AuthenticationMethods.Password);
        var expires = DateTime.UtcNow.AddMinutes(7);
        GivenReceipt(new EmailCodeSendReceipt(SendCount: 1, expires));

        var sent = await Sender().SendAsync(_user, challenge, "203.0.113.7", hardLimit: 100, CancellationToken.None);

        sent.IsError.Should().BeFalse();
        sent.Value.Should().Be(new EmailCodeSent(expires, "o***r@example.com", SendsRemaining: 2));
        _reserved.Should().Be(new EmailCodeSendReservation(
            challenge.Id, TwoFactorChallengePurpose.SignIn, MacOf(challenge.Id, Code), LifetimeMinutes: 7,
            RequireBoundFactor: true, HardLimit: 100, WindowSeconds: 60, WindowMax: 3, DailyMax: 10));
        _sent!.TypeCode.Should().Be(NotificationTypeCodes.TwoFactorEmailCode);
        _sent.Variables["OtpCode"].Should().Be(Code);
        _sent.Variables["ExpirationMinutes"].Should().Be(7);
        _sent.RecipientAddress.Should().Be("owner@example.com");
        _logger.Entries.Should().NotContain(entry => entry.Message.Contains(Code) || entry.Message.Contains("owner@example.com"),
            "the code lives in the email alone, and the address in the account row");
    }

    // Review of #107 (F2): the email says what the code is for — a code to switch the
    // emailed code on or off must never read "someone entered your password to sign in".
    [Theory]
    [InlineData(TwoFactorChallengePurpose.SignIn, TwoFactorEmailCodePurposes.SignIn)]
    [InlineData(TwoFactorChallengePurpose.EmailEnrollment, TwoFactorEmailCodePurposes.Enrollment)]
    [InlineData(TwoFactorChallengePurpose.EmailDisable, TwoFactorEmailCodePurposes.SwitchOff)]
    [InlineData(TwoFactorChallengePurpose.DeletionRecovery, TwoFactorEmailCodePurposes.DeletionRecovery)]
    public async Task TheEmail_NamesWhatTheCodeIsFor(TwoFactorChallengePurpose purpose, string expected)
    {
        var challenge = ChallengeWith(_user.Id, AuthenticationMethods.Password, purpose);
        GivenReceipt(new EmailCodeSendReceipt(SendCount: 1, DateTime.UtcNow.AddMinutes(7)));

        (await Sender().SendAsync(_user, challenge, "203.0.113.7", hardLimit: 100, CancellationToken.None)).IsError.Should().BeFalse();

        _sent!.Variables["Purpose"].Should().Be(expected);
    }

    [Fact]
    public async Task TheLastSend_SaysNoneRemain()
    {
        GivenReceipt(new EmailCodeSendReceipt(SendCount: TwoFactorChallenge.MaxEmailCodeSends, DateTime.UtcNow));

        var sent = await Sender().SendAsync(_user, ChallengeWith(_user.Id, AuthenticationMethods.Password), null, 100, CancellationToken.None);

        sent.Value.SendsRemaining.Should().Be(0);
    }

    [Fact]
    public async Task WithEmailOff_NothingIsReservedOrSent_AndTheSignalSaysWhy()
    {
        _email.Enabled = false;

        var sent = await Sender().SendAsync(_user, ChallengeWith(_user.Id, AuthenticationMethods.Password), null, 100, CancellationToken.None);

        sent.FirstError.Should().Be(TwoFactorErrors.EmailCodeUnavailable);
        _challenges.Verify(c => c.TryReserveEmailCodeSendAsync(It.IsAny<EmailCodeSendReservation>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Values.Contains(TwoFactorSignalCodes.EmailCodeUnavailable));
    }

    [Fact]
    public void TheSender_ReadsEmailEnabled_PerRequest()
    {
        // A snapshot is built per request scope; a monitor or plain options captured by a
        // longer-lived service would keep sending after the switch was turned off.
        typeof(EmailCodeSender).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should().Contain(typeof(IOptionsSnapshot<EmailSettings>))
            .And.Contain(typeof(IOptionsSnapshot<TwoFactorSettings>))
            .And.NotContain(typeof(IOptions<EmailSettings>))
            .And.NotContain(typeof(IOptionsMonitor<EmailSettings>));
    }

    public static TheoryData<TwoFactorSnapshot?, bool, string> Refusals(Guid userId) => new()
    {
        { EmailRow(userId, emailFailedAttempts: 100), true, TwoFactorErrors.EmailCodeLocked.Code },
        { EmailRow(userId, emailFailedAttempts: 5, emailLockedUntil: DateTime.UtcNow.AddMinutes(10)), true, TwoFactorErrors.LockedOut.Code },
        { EmailRow(userId), true, TwoFactorErrors.EmailCodeTooManyRequests.Code },
        { TotpRow(userId), true, TwoFactorErrors.EmailCodeUnavailable.Code },
        { EmailRow(userId), false, TwoFactorErrors.ChallengeInvalid.Code },
    };

    [Fact]
    public async Task ARefusedReservation_IsNamedByWhatRefusedIt()
    {
        foreach (var row in Refusals(_user.Id))
        {
            var (snapshot, live, expected) = ((TwoFactorSnapshot?)row[0], (bool)row[1], (string)row[2]);
            _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            GivenReceipt(null);
            var challenge = live
                ? ChallengeWith(_user.Id, AuthenticationMethods.Password)
                : new TwoFactorChallenge(Guid.NewGuid(), _user.Id, "h", null, DateTime.UtcNow.AddMinutes(-1), null, 0,
                    DateTime.UtcNow.AddMinutes(-10), AuthenticationMethods.Password);

            var sent = await Sender().SendAsync(_user, challenge, null, 100, CancellationToken.None);

            sent.FirstError.Code.Should().Be(expected);
            _notifications.Verify(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    // ── The anonymous handler in front of it ──

    private SendSignInEmailCodeCommandHandler Handler(TwoFactorChallenge? challenge, Mock<IEmailCodeSender> sender)
    {
        var keys = new FakeCredentialHasher();
        _challenges.Setup(c => c.GetByTokenHashAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        return new SendSignInEmailCodeCommandHandler(
            _challenges.Object, _store.Object, users.Object, keys, sender.Object, TestHelpers.CreateSecondFactorLockout());
    }

    [Fact]
    public async Task APasswordSignIn_OnAnEmailFactorAccount_SendsUnderTheHardLimit()
    {
        var challenge = ChallengeWith(_user.Id, AuthenticationMethods.Password);
        var sender = new Mock<IEmailCodeSender>();
        sender.Setup(s => s.SendAsync(_user, challenge, "203.0.113.7", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailCodeSent(DateTime.UtcNow, "o***r@example.com", 2));

        var result = await Handler(challenge, sender).Handle(
            new SendSignInEmailCodeCommand("token", "203.0.113.7"), CancellationToken.None);

        result.Value.SendsRemaining.Should().Be(2);
        result.Value.MaskedEmail.Should().Be("o***r@example.com");
    }

    [Fact]
    public async Task AProviderSignIn_OnAnEmailFactorAccount_IsSentACode()
    {
        // D-243: after a provider that does not control the account's mailbox (or at
        // another address), the emailed code is the second step, and this its resend.
        var challenge = ChallengeWith(_user.Id, AuthenticationMethods.ExternalIdentity);
        var sender = new Mock<IEmailCodeSender>();
        sender.Setup(s => s.SendAsync(_user, challenge, "203.0.113.7", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailCodeSent(DateTime.UtcNow, "o***r@example.com", 1));

        var result = await Handler(challenge, sender).Handle(
            new SendSignInEmailCodeCommand("token", "203.0.113.7"), CancellationToken.None);

        result.Value.SendsRemaining.Should().Be(1);
    }

    [Theory]
    [InlineData(4)] // a verify-email link
    [InlineData(0)] // a challenge recorded before first factors were
    public async Task AfterAFirstFactorThatWentThroughTheMailbox_NoCodeIsSent(int primary)
    {
        var sender = new Mock<IEmailCodeSender>();

        var result = await Handler(ChallengeWith(_user.Id, AuthenticationMethods.FromStored(primary)), sender).Handle(
            new SendSignInEmailCodeCommand("token", null), CancellationToken.None);

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeUnavailable);
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnAuthenticatorAppAccount_IsSentNothing()
    {
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(TotpRow(_user.Id));
        var sender = new Mock<IEmailCodeSender>();

        var result = await Handler(ChallengeWith(_user.Id, AuthenticationMethods.Password), sender).Handle(
            new SendSignInEmailCodeCommand("token", null), CancellationToken.None);

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeUnavailable);
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownOrDeadChallenge_IsOneOpaqueAnswer()
    {
        var sender = new Mock<IEmailCodeSender>();

        var result = await Handler(null, sender).Handle(new SendSignInEmailCodeCommand("token", null), CancellationToken.None);

        result.FirstError.Should().Be(TwoFactorErrors.ChallengeInvalid);
        sender.VerifyNoOtherCalls();
    }

    // ── The email channel while email is off ──

    private static (EmailNotificationChannel Channel, CapturingLogger<EmailNotificationChannel> Logger) Channel()
    {
        var settings = TestHelpers.CreateOptions(new EmailSettings { Enabled = false });
        var environment = new Mock<IEnvironmentInfo>();
        environment.Setup(e => e.IsDevelopment).Returns(true);
        var logger = new CapturingLogger<EmailNotificationChannel>();
        return (new EmailNotificationChannel(
            settings, new SmtpEmailSender(settings, Mock.Of<ILogger<SmtpEmailSender>>()), environment.Object, logger), logger);
    }

    private static RenderedNotification Rendered(string typeCode) => new()
    {
        Channel = NotificationChannelType.Email,
        RecipientAddress = "owner@example.com",
        NotificationTypeCode = typeCode,
        LanguageCode = "en",
        Subject = "Your sign-in code",
        BodyHtml = $"<p>{Code}</p>",
        BodyText = Code
    };

    [Fact]
    public async Task WithEmailOff_TheEmailedCodeIsRefused_BeforeItsBodyCouldBeLogged()
    {
        var (channel, logger) = Channel();

        var result = await channel.SendAsync(Rendered(NotificationTypeCodes.TwoFactorEmailCode), CancellationToken.None);

        result.FirstError.Should().Be(NotificationErrors.SendFailed, "the row then retries and dies, instead of reading Sent");
        logger.Entries.Should().NotContain(entry => entry.Message.Contains(Code) || entry.Values.Any(value => value.Contains(Code)),
            "not even the Development body line may carry the code");
    }

    [Fact]
    public async Task WithEmailOff_EveryOtherType_KeepsTheDevelopmentBehaviour()
    {
        var (channel, logger) = Channel();

        var result = await channel.SendAsync(Rendered(NotificationTypeCodes.PasswordReset), CancellationToken.None);

        result.IsError.Should().BeFalse();
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Debug);
    }

    [Fact]
    public void OnlyTheEmailedSecondFactorCode_MustReallyBeDelivered_AndItIsSensitive()
    {
        NotificationTypeCodes.DeliveryRequiredCodes.Should().BeEquivalentTo([NotificationTypeCodes.TwoFactorEmailCode]);
        NotificationTypeCodes.SensitiveContentCodes.Should().Contain(NotificationTypeCodes.TwoFactorEmailCode);
        NotificationTypeCodes.SystemCodes.Should().Contain(NotificationTypeCodes.TwoFactorEmailCode);
    }
}
