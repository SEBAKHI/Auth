using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Passkeys.BeginPasskeyRegistration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.CompletePasskeyRegistration;
using Auth.Application.Features.Passkeys.SendPasskeyRegistrationEmailCode;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK7, PK14, T-A4, ARR-1049): adding a passkey. The session rule runs before anything is
/// minted, counted or written: a recent sign-in; one that proved a first factor; two factors
/// when the account already holds a qualifying one (FA2); and, when it holds none and email is
/// on, the code emailed to its confirmed address, checked before the bind. The challenge is
/// spent before the response is checked, and the limit of ten is enforced by the insert.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the <c>HasPrimary</c> step dropped from the gate; the
/// FA2 step dropped from the gate.
/// </remarks>
public class RegisterPasskeyCommandHandlerTests
{
    private const string GeneratedHandle = "generated-handle";
    private const string Handle = "challenge-handle";
    private const string OptionsJson = "{\"challenge\":\"c2Vzc2lvbg\"}";
    private const string ResponseJson = "{\"id\":\"AQIDBA\"}";
    private const string EmailCode = "123456";
    private const string StoredHash = "stored-hash";
    private const string DeviceName = "Firefox on Linux";

    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly byte[] CredentialId = [1, 2, 3, 4];

    private static readonly QualifyingSecondFactors AppOnly = new(Totp: true, Passkey: false);
    private static readonly QualifyingSecondFactors PasskeyOnly = new(Totp: false, Passkey: true);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IReauthenticationGuard> _guard = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserPasskeyRepository> _passkeys = new();
    private readonly Mock<IWebAuthnChallengeRepository> _challenges = new();
    private readonly Mock<IWebAuthnVerifier> _verifier = new();
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<ITwoFactorBindCodeRepository> _bindCodes = new();
    private readonly Mock<IOtpHasher> _otpHasher = new();
    private readonly Mock<IOtpGenerator> _otpGenerator = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly FakeCredentialHasher _hasher = new();
    private readonly EmailSettings _email = new() { Enabled = false };
    private readonly List<string> _order = [];

    public RegisterPasskeyCommandHandlerTests()
    {
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId, email: "owner@example.com"));
        GivenSession(AuthenticationMethods.Password);
        GivenFactors(QualifyingSecondFactors.None);

        _passkeys.Setup(r => r.GetLiveAsync(_userId, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _passkeys.Setup(r => r.EnsureUserHandleAsync(_userId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, byte[] candidate, CancellationToken _) => candidate);
        _passkeys.Setup(r => r.TryCreateAsync(It.IsAny<UserPasskey>(), UserPasskey.MaxLivePerAccount, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _order.Add("insert"))
            .ReturnsAsync(PasskeyInsertOutcome.Inserted);

        _tokens.Setup(t => t.Generate()).Returns(GeneratedHandle);
        _verifier.Setup(v => v.CreateRegistrationOptions(It.IsAny<PasskeyRelyingParty>(), It.IsAny<PasskeyRegistrationSubject>(), It.IsAny<IReadOnlyList<byte[]>>()))
            .Returns(OptionsJson);
        _challenges.Setup(c => c.TryConsumeRegistrationAsync(It.IsAny<CredentialHashCandidates>(), _userId, It.IsAny<CancellationToken>()))
            .Callback(() => _order.Add("consume"))
            .ReturnsAsync(OptionsJson);
        _verifier.Setup(v => v.VerifyRegistrationAsync(It.IsAny<PasskeyRelyingParty>(), OptionsJson, ResponseJson, It.IsAny<CancellationToken>()))
            .Callback(() => _order.Add("verify"))
            .ReturnsAsync(new VerifiedPasskeyCredential(CredentialId, [5, 6], 0, IsBackupEligible: true, IsBackedUp: false));

        _otpGenerator.Setup(g => g.GenerateNumericOtp(It.IsAny<int>())).Returns("654321");
        _otpHasher.Setup(h => h.Hash(It.IsAny<string>(), It.IsAny<string>())).Returns("fresh-hash");
        _notifications.Setup(n => n.SendAsync(It.IsAny<Auth.Application.Notifications.NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Result.Success);
    }

    // ── The session rule (begin; the same gate runs at verify and at the send) ──

    [Fact]
    public async Task Begin_WithAPasswordSession_AndNoFactor_OffersOptions_AndStoresOnlyTheHandlesFingerprint()
    {
        var started = await Begin().Handle(BeginCommand(), CancellationToken.None);

        started.IsError.Should().BeFalse();
        started.Value.OptionsJson.Should().Be(OptionsJson);
        started.Value.ChallengeHandle.Should().Be(GeneratedHandle);
        started.Value.EmailCodeRequired.Should().BeFalse("email is off: the independent notice alone (FA11)");
        _challenges.Verify(c => c.CreateAsync(
            It.Is<WebAuthnChallenge>(challenge =>
                challenge.UserId == _userId
                && challenge.Purpose == WebAuthnChallengePurpose.Registration
                && challenge.HandleHash == _hasher.Of(GeneratedHandle)
                && challenge.HandleHash != GeneratedHandle
                && challenge.OptionsJson == OptionsJson
                && challenge.ExpiresAt == challenge.CreatedAt + WebAuthnChallenge.Lifetime),
            It.IsAny<CancellationToken>()), Times.Once);
        _hasher.Hashed.Should().Contain((CredentialPurpose.WebAuthnChallenge, GeneratedHandle));
    }

    [Fact]
    public async Task Begin_WithEmailOn_AndNoFactor_SaysTheBindNeedsTheEmailedCode()
    {
        _email.Enabled = true;

        var started = await Begin().Handle(BeginCommand(), CancellationToken.None);

        started.Value.EmailCodeRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Begin_PasswordAccountSignedInByEmailedCode_IsAskedToSignInAgain_WithNothingWritten()
    {
        // T-A4 (D-215-1, REVIEW 110 F5): the code proved the mailbox only.
        GivenSession(AuthenticationMethods.EmailCode);

        var started = await Begin().Handle(BeginCommand(), CancellationToken.None);

        started.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _passkeys.Verify(r => r.EnsureUserHandleAsync(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        _challenges.Verify(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Begin_PasswordlessAccountsEmailedCode_IsItsFirstFactor_AndIsAllowed()
    {
        // D-S20-2 (a): nothing removes its address, so a passkey is never its only way in.
        GivenSession(AuthenticationMethods.EmailCode.With(AuthenticationMethods.EmailCodeFirstFactor));

        (await Begin().Handle(BeginCommand(), CancellationToken.None)).IsError.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(FactorsThatNeedTwo))]
    public async Task Begin_BesideAQualifyingFactor_WithoutATwoFactorSession_IsRefused(QualifyingSecondFactors factors)
    {
        GivenFactors(factors);

        var started = await Begin().Handle(BeginCommand(), CancellationToken.None);

        started.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code, "a further factor needs the proof the account already has (FA11 (11-2))");
        _challenges.Verify(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<QualifyingSecondFactors> FactorsThatNeedTwo => new() { AppOnly, PasskeyOnly };

    [Fact]
    public async Task Begin_BesideAQualifyingFactor_FromATwoFactorSession_AsksNoEmailedCode()
    {
        _email.Enabled = true;
        GivenFactors(AppOnly);
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        var started = await Begin().Handle(BeginCommand(), CancellationToken.None);

        started.IsError.Should().BeFalse();
        started.Value.EmailCodeRequired.Should().BeFalse("the two-factor session is the proof of a further factor");
    }

    [Fact]
    public async Task Begin_WithoutARecentSignIn_IsRefusedFirst()
    {
        _guard.Setup(g => g.EnsureRecentSignInAsync(_userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);

        var started = await Begin().Handle(BeginCommand(), CancellationToken.None);

        started.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _store.Verify(s => s.GetQualifyingFactorsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Begin_WhilePasskeysAreOff_IsRefused_BeforeTheSessionIsRead()
    {
        var started = await Begin(PasskeyTestDoubles.Feature(enabled: false)).Handle(BeginCommand(), CancellationToken.None);

        started.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        _guard.Verify(g => g.EnsureRecentSignInAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Begin: the limit, the handle, the exclusions ──

    [Fact]
    public async Task Begin_WithTenLivePasskeys_IsRefused_WithTheLimit()
    {
        var ten = Enumerable.Range(0, UserPasskey.MaxLivePerAccount)
            .Select(_ => PasskeyTestDoubles.Live(_userId, DateTime.UtcNow.AddDays(-10)))
            .ToList();
        _passkeys.Setup(r => r.GetLiveAsync(_userId, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>())).ReturnsAsync(ten);

        var started = await Begin().Handle(BeginCommand(), CancellationToken.None);

        started.FirstError.Code.Should().Be(PasskeyErrors.LimitReached.Code);
        _challenges.Verify(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Begin_OffersTheStoredUserHandle_AndExcludesTheLivePasskeys()
    {
        byte[] stored = [.. Enumerable.Repeat((byte)7, 64)];
        var live = new List<UserPasskey>
        {
            PasskeyTestDoubles.Live(_userId, DateTime.UtcNow.AddDays(-3), "Phone"),
            PasskeyTestDoubles.Live(_userId, DateTime.UtcNow.AddDays(-2), "Laptop"),
        };
        byte[]? candidate = null;
        _passkeys.Setup(r => r.GetLiveAsync(_userId, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>())).ReturnsAsync(live);
        _passkeys.Setup(r => r.EnsureUserHandleAsync(_userId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, byte[] offered, CancellationToken _) => candidate = offered)
            .ReturnsAsync(stored);

        await Begin().Handle(BeginCommand(), CancellationToken.None);

        candidate.Should().HaveCount(64, "a random handle of 64 bytes, kept only if the account has none (PK17)");
        _verifier.Verify(v => v.CreateRegistrationOptions(
            It.Is<PasskeyRelyingParty>(rp => rp.RpId == PasskeyTestDoubles.RpId),
            It.Is<PasskeyRegistrationSubject>(subject => subject.UserHandle.SequenceEqual(stored) && subject.Name == "owner@example.com"),
            It.Is<IReadOnlyList<byte[]>>(excluded => excluded.Count == 2
                && excluded[0].SequenceEqual(live[0].CredentialId) && excluded[1].SequenceEqual(live[1].CredentialId))), Times.Once);
    }

    // ── Verify ──

    [Fact]
    public async Task Complete_SpendsTheChallengeBeforeChecking_StoresThePasskey_AndPublishesTheEvent()
    {
        var created = await Complete().Handle(CompleteCommand(), CancellationToken.None);

        created.IsError.Should().BeFalse();
        created.Value.Name.Should().Be("Laptop");
        _order.Should().Equal("consume", "verify", "insert");
        _hasher.Looked.Should().Contain((CredentialPurpose.WebAuthnChallenge, Handle));
        _passkeys.Verify(r => r.TryCreateAsync(
            It.Is<UserPasskey>(passkey => passkey.UserId == _userId
                && passkey.RpId == PasskeyTestDoubles.RpId
                && passkey.IsUserVerified
                && passkey.CredentialId.SequenceEqual(CredentialId)
                && passkey.Name == "Laptop"),
            UserPasskey.MaxLivePerAccount, null, CancellationToken.None), Times.Once);
        _publisher.Verify(p => p.Publish(
            It.Is<PasskeyRegisteredEvent>(e => e.UserId == _userId && e.RegisteredBy == _userId && e.Name == "Laptop"
                && e.Email == "owner@example.com" && e.DeviceName == DeviceName),
            CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(Handle, true)]
    public async Task Complete_WithNoChallengeCookie_OrTwo_IsRefused_WithNothingSpent(string? handle, bool repeated)
    {
        // Two cookies: one may be a sibling host's plant (ARR-0687).
        var created = await Complete().Handle(CompleteCommand(handle: handle, repeated: repeated), CancellationToken.None);

        created.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        _challenges.Verify(c => c.TryConsumeRegistrationAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _guard.Verify(g => g.EnsureRecentSignInAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_AResponseThatDoesNotVerify_SpendsTheChallenge_AndStoresNothing()
    {
        _verifier.Setup(v => v.VerifyRegistrationAsync(It.IsAny<PasskeyRelyingParty>(), OptionsJson, ResponseJson, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasskeyErrors.VerificationFailed);

        var created = await Complete().Handle(CompleteCommand(), CancellationToken.None);

        created.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        _challenges.Verify(c => c.TryConsumeRegistrationAsync(It.IsAny<CredentialHashCandidates>(), _userId, It.IsAny<CancellationToken>()), Times.Once);
        AssertNothingStored();
    }

    [Fact]
    public async Task Complete_WithoutALiveChallenge_IsRefused_WithoutCheckingTheResponse()
    {
        _challenges.Setup(c => c.TryConsumeRegistrationAsync(It.IsAny<CredentialHashCandidates>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var created = await Complete().Handle(CompleteCommand(), CancellationToken.None);

        created.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        _verifier.Verify(v => v.VerifyRegistrationAsync(It.IsAny<PasskeyRelyingParty>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertNothingStored();
    }

    [Fact]
    public async Task Complete_BesideAQualifyingFactor_WithoutATwoFactorSession_IsRefused_WithNothingSpent()
    {
        GivenFactors(PasskeyOnly);

        var created = await Complete().Handle(CompleteCommand(), CancellationToken.None);

        created.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _challenges.Verify(c => c.TryConsumeRegistrationAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_EmailOn_NoFactor_WithoutTheCode_AsksForIt_WithNothingSpent()
    {
        _email.Enabled = true;

        var created = await Complete().Handle(CompleteCommand(emailCode: null), CancellationToken.None);

        created.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeRequired.Code);
        _bindCodes.Verify(r => r.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _challenges.Verify(c => c.TryConsumeRegistrationAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_EmailOn_NoFactor_AWrongCode_IsRefused_BeforeTheChallengeIsSpent()
    {
        _email.Enabled = true;
        var code = GivenLiveCode();

        var created = await Complete().Handle(CompleteCommand(emailCode: "000000"), CancellationToken.None);

        created.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        _bindCodes.Verify(r => r.TryReserveAttemptAsync(code.Id, TwoFactorBindCode.MaxAttempts, It.IsAny<CancellationToken>()), Times.Once,
            "the attempt is counted before the code is checked");
        _challenges.Verify(c => c.TryConsumeRegistrationAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertNothingStored();
    }

    [Fact]
    public async Task Complete_EmailOn_NoFactor_TheRightCode_IsSpentByTheInsertsTransaction()
    {
        _email.Enabled = true;
        var code = GivenLiveCode();

        var created = await Complete().Handle(CompleteCommand(emailCode: EmailCode), CancellationToken.None);

        created.IsError.Should().BeFalse();
        _passkeys.Verify(r => r.TryCreateAsync(It.IsAny<UserPasskey>(), UserPasskey.MaxLivePerAccount, code.Id, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Complete_TheCodeSpentByAnotherRequest_IsRefused_WithNoEvent()
    {
        _email.Enabled = true;
        GivenLiveCode();
        GivenInsertOutcome(PasskeyInsertOutcome.BindCodeLost);

        var created = await Complete().Handle(CompleteCommand(emailCode: EmailCode), CancellationToken.None);

        created.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        _publisher.Verify(p => p.Publish(It.IsAny<PasskeyRegisteredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_EmailOff_Binds_WithTheNoticeAlone()
    {
        var created = await Complete().Handle(CompleteCommand(emailCode: null), CancellationToken.None);

        created.IsError.Should().BeFalse();
        _passkeys.Verify(r => r.TryCreateAsync(It.IsAny<UserPasskey>(), UserPasskey.MaxLivePerAccount, null, CancellationToken.None), Times.Once);
        _publisher.Verify(p => p.Publish(It.IsAny<PasskeyRegisteredEvent>(), CancellationToken.None), Times.Once,
            "the owner's independent notice follows the event");
    }

    [Theory]
    [InlineData(PasskeyInsertOutcome.LimitReached, "Passkey.LimitReached")]
    [InlineData(PasskeyInsertOutcome.DuplicateCredential, "Passkey.VerificationFailed")]
    public async Task Complete_AnInsertThatLoses_IsRefused_WithNoEvent(PasskeyInsertOutcome outcome, string code)
    {
        // The eleventh: two registrations racing for the tenth place, one is refused by the insert.
        GivenInsertOutcome(outcome);

        var created = await Complete().Handle(CompleteCommand(), CancellationToken.None);

        created.FirstError.Code.Should().Be(code);
        _publisher.Verify(p => p.Publish(It.IsAny<PasskeyRegisteredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_SixtyFourEmoji_AreANameAndSixtyFiveAreNot()
    {
        var sixtyFour = string.Concat(Enumerable.Repeat("😀", 64));
        var sixtyFive = sixtyFour + "😀";

        (await Complete().Handle(CompleteCommand(name: sixtyFour), CancellationToken.None)).Value.Name.Should().Be(sixtyFour);
        (await Complete().Handle(CompleteCommand(name: sixtyFive), CancellationToken.None)).FirstError.Code
            .Should().Be(PasskeyErrors.NameTooLong.Code);

        var validator = new CompletePasskeyRegistrationCommandValidator();
        validator.Validate(CompleteCommand(name: sixtyFour)).IsValid.Should().BeTrue("64 code points, 128 UTF-16 units (T-A8)");
        validator.Validate(CompleteCommand(name: sixtyFive)).Errors.Should().ContainSingle()
            .Which.ErrorCode.Should().Be(PasskeyErrors.NameTooLong.Code);
    }

    // ── The emailed code's send step (T-A6) ──

    [Fact]
    public async Task Send_EmailOff_SaysNoCodeIsNeeded_AndMintsNone()
    {
        var sent = await Send().Handle(SendCommand(), CancellationToken.None);

        sent.Value.EmailCodeRequired.Should().BeFalse();
        _challenges.Verify(c => c.HasLiveRegistrationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _bindCodes.Verify(r => r.CreateAsync(It.IsAny<TwoFactorBindCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_BesideAQualifyingFactor_FromATwoFactorSession_SaysNoCodeIsNeeded()
    {
        _email.Enabled = true;
        GivenFactors(AppOnly);
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        var sent = await Send().Handle(SendCommand(), CancellationToken.None);

        sent.Value.EmailCodeRequired.Should().BeFalse();
        _bindCodes.Verify(r => r.CreateAsync(It.IsAny<TwoFactorBindCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_EmailOn_WithoutAStartedRegistration_IsRefused_AndMintsNone()
    {
        // As the app's sender mints only while its setup is pending (Fact 7).
        _email.Enabled = true;

        var sent = await Send().Handle(SendCommand(), CancellationToken.None);

        sent.FirstError.Code.Should().Be(TwoFactorErrors.SetupRequired.Code);
        _bindCodes.Verify(r => r.CreateAsync(It.IsAny<TwoFactorBindCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_EmailOn_DuringARegistration_EmailsTheCode_ToTheConfirmedAddress()
    {
        _email.Enabled = true;
        _challenges.Setup(c => c.HasLiveRegistrationAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sent = await Send().Handle(SendCommand(), CancellationToken.None);

        sent.IsError.Should().BeFalse();
        sent.Value.EmailCodeRequired.Should().BeTrue();
        sent.Value.SentTo.Should().NotBe("owner@example.com", "the address is masked");
        _bindCodes.Verify(r => r.CreateAsync(It.Is<TwoFactorBindCode>(code => code.UserId == _userId), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.SendAsync(
            It.Is<Auth.Application.Notifications.NotificationRequest>(request => request.TypeCode == NotificationTypeCodes.TwoFactorBindCode
                && request.RecipientAddress == "owner@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_FromAPasswordAccountsEmailedCodeSession_IsRefused()
    {
        _email.Enabled = true;
        GivenSession(AuthenticationMethods.EmailCode);

        var sent = await Send().Handle(SendCommand(), CancellationToken.None);

        sent.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
    }

    // ── Fixture ──

    private void GivenSession(AuthenticationMethods methods) =>
        _guard.Setup(g => g.EnsureRecentSignInAsync(_userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, DeviceName, methods));

    private void GivenFactors(QualifyingSecondFactors factors) =>
        _store.Setup(s => s.GetQualifyingFactorsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(factors);

    private void GivenInsertOutcome(PasskeyInsertOutcome outcome) =>
        _passkeys.Setup(r => r.TryCreateAsync(It.IsAny<UserPasskey>(), UserPasskey.MaxLivePerAccount, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    private TwoFactorBindCode GivenLiveCode()
    {
        var code = new TwoFactorBindCode(
            Guid.NewGuid(), _userId, StoredHash, DateTime.UtcNow.AddMinutes(10), null, 0, "203.0.113.7", DateTime.UtcNow);
        _bindCodes.Setup(r => r.GetLiveForUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(code);
        _bindCodes.Setup(r => r.TryReserveAttemptAsync(code.Id, TwoFactorBindCode.MaxAttempts, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _otpHasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string, string>((scope, submitted, stored) =>
                scope == FirstFactorEmailProof.HashScope(_userId) && submitted == EmailCode && stored == StoredHash);
        return code;
    }

    private void AssertNothingStored()
    {
        _passkeys.Verify(r => r.TryCreateAsync(It.IsAny<UserPasskey>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<PasskeyRegisteredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private PasskeyRegistrationGate Gate() =>
        new(_guard.Object, _store.Object, new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(_email)),
            NullLogger<PasskeyRegistrationGate>.Instance);

    private FirstFactorEmailProof EmailProof() =>
        new(_bindCodes.Object, _users.Object, _notifications.Object, _otpGenerator.Object, _otpHasher.Object,
            TestHelpers.CreateOptions(_email), TimeProvider.System, NullLogger<FirstFactorEmailProof>.Instance);

    private BeginPasskeyRegistrationCommandHandler Begin(PasskeyFeature? feature = null) =>
        new(feature ?? PasskeyTestDoubles.Feature(), Gate(), _users.Object, _passkeys.Object, _challenges.Object,
            _verifier.Object, _tokens.Object, _hasher, TimeProvider.System);

    private CompletePasskeyRegistrationCommandHandler Complete() =>
        new(PasskeyTestDoubles.Feature(), Gate(), _users.Object, _passkeys.Object, _challenges.Object, _verifier.Object,
            _hasher, EmailProof(), _publisher.Object, TimeProvider.System, NullLogger<CompletePasskeyRegistrationCommandHandler>.Instance);

    private SendPasskeyRegistrationEmailCodeCommandHandler Send() =>
        new(PasskeyTestDoubles.Feature(), Gate(), _challenges.Object, EmailProof());

    private BeginPasskeyRegistrationCommand BeginCommand() => new(_userId, SessionId);

    private CompletePasskeyRegistrationCommand CompleteCommand(
        string? handle = Handle, bool repeated = false, string? name = "Laptop", string? emailCode = null) =>
        new(_userId, SessionId, handle, repeated, ResponseJson, name, emailCode);

    private SendPasskeyRegistrationEmailCodeCommand SendCommand() => new(_userId, SessionId, "203.0.113.7");
}
