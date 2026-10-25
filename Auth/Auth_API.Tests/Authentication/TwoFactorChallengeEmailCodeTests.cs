using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.SendSignInEmailCode;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// D-243 (OI-243): a challenge that offers the emailed code emails it as it is opened,
/// once, and a refused or failed send never fails the sign-in; a provider sign-in that
/// controls the account's own mailbox asks an emailed-code account for nothing more;
/// and the emailed code follows the password or a provider, never a verify-email link.
/// </summary>
public class TwoFactorChallengeEmailCodeTests
{
    private const string AccountAddress = "person@example.com";

    private static readonly EmailCodeSent Sent =
        new(new DateTime(2026, 10, 26, 12, 10, 0, DateTimeKind.Utc), "p****n@example.com", 2);

    private readonly User _user = TestHelpers.CreateUser(email: AccountAddress, twoFactorEnabled: true);
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IEmailCodeSender> _sender = new();

    private TwoFactorChallengeService Service(bool emailEnabled = true)
    {
        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateRefreshToken()).Returns("token");

        return new TwoFactorChallengeService(
            Mock.Of<ITwoFactorChallengeRepository>(),
            Mock.Of<ILoginAttemptRepository>(),
            jwt.Object,
            new FakeCredentialHasher(),
            _store.Object,
            TestHelpers.CreateOptions(new EmailSettings { Enabled = emailEnabled }),
            _sender.Object,
            TestHelpers.CreateSecondFactorLockout(),
            Mock.Of<ILogger<TwoFactorChallengeService>>());
    }

    private void GivenRow(TwoFactorSnapshot? row) =>
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

    private void GivenSend(ErrorOr<EmailCodeSent> answer) =>
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer);

    private void VerifySends(Times times) =>
        _sender.Verify(s => s.SendAsync(It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), times);

    private static TwoFactorSnapshot BothFactorsRow(Guid userId) =>
        new(userId, "v2:ciphertext", null, isEnabled: true, failedAttempts: 0, lockedUntil: null,
            modifiedAt: DateTime.UtcNow.AddDays(-1), emailCodeEnabledAt: DateTime.UtcNow.AddDays(-30));

    // ── T1, V1: the send ──

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AChallengeOfferingTheEmailedCode_EmailsItOnce_ForItsOwnChallenge_AndReturnsWhereItWent(int primary)
    {
        GivenRow(EmailRow(_user.Id));
        GivenSend(Sent);

        var issued = await Service().CreateChallengeAsync(
            _user, "203.0.113.7", "agent", AuthenticationMethods.FromStored(primary), CancellationToken.None);

        issued.Methods.Should().Equal(TwoFactorMethodNames.Email, TwoFactorMethodNames.Recovery);
        issued.EmailCode.Should().Be(Sent);
        _sender.Verify(s => s.SendAsync(
            _user,
            It.Is<TwoFactorChallenge>(c => c.UserId == _user.Id && c.PrimaryMethod == AuthenticationMethods.FromStored(primary)),
            "203.0.113.7",
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public static TheoryData<string, int, bool> NoSend => new()
    {
        { "authenticator app", 1, true },
        { "authenticator app after a provider", 2, true },
        { "emailed code with email off", 1, false },
        { "emailed code after a verify-email link", 4, true },
        { "no factor row", 1, true },
    };

    [Theory]
    [MemberData(nameof(NoSend))]
    public async Task AChallengeNotOfferingTheEmailedCode_SendsNothing(string row, int primary, bool emailEnabled)
    {
        GivenRow(row switch
        {
            "no factor row" => null,
            _ when row.StartsWith("authenticator", StringComparison.Ordinal) => TotpRow(_user.Id),
            _ => EmailRow(_user.Id)
        });
        GivenSend(Sent);

        var issued = await Service(emailEnabled).CreateChallengeAsync(
            _user, null, null, AuthenticationMethods.FromStored(primary), CancellationToken.None);

        issued.EmailCode.Should().BeNull(row);
        VerifySends(Times.Never());
    }

    // ── T1, V2: a refused or failed send ──

    public static TheoryData<string> Refusals => new()
    {
        TwoFactorErrors.EmailCodeTooManyRequests.Code,
        TwoFactorErrors.EmailCodeUnavailable.Code,
        TwoFactorErrors.EmailCodeLocked.Code,
        TwoFactorErrors.LockedOut.Code,
        TwoFactorErrors.EmailCodeSendFailed.Code,
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ARefusedSend_StillReturnsTheChallengeAndItsMethods_WithNoCode(string refusal)
    {
        GivenRow(EmailRow(_user.Id));
        GivenSend(Error.Custom(403, refusal, refusal));

        var issued = await Service().CreateChallengeAsync(
            _user, null, null, AuthenticationMethods.Password, CancellationToken.None);

        issued.Token.Should().Be("token");
        issued.Methods.Should().Equal(TwoFactorMethodNames.Email, TwoFactorMethodNames.Recovery);
        issued.EmailCode.Should().BeNull("the client then asks for a code, and its button answers the refusal precisely");
    }

    [Fact]
    public async Task ASendThatThrows_StillReturnsTheChallenge_WithNoCode()
    {
        GivenRow(EmailRow(_user.Id));
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the database went away"));

        var issued = await Service().CreateChallengeAsync(
            _user, null, null, AuthenticationMethods.Password, CancellationToken.None);

        issued.Token.Should().Be("token");
        issued.EmailCode.Should().BeNull();
    }

    [Fact]
    public async Task TheRequestsOwnCancellation_IsNotSwallowed()
    {
        GivenRow(EmailRow(_user.Id));
        using var aborted = new CancellationTokenSource();
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback(() => aborted.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        var act = () => Service().CreateChallengeAsync(_user, null, null, AuthenticationMethods.Password, aborted.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnotherCancellation_ATimeout_StillReturnsTheChallenge_WithNoCode()
    {
        GivenRow(EmailRow(_user.Id));
        _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("a command timeout"));

        var issued = await Service().CreateChallengeAsync(_user, null, null, AuthenticationMethods.Password, CancellationToken.None);

        issued.Token.Should().Be("token");
        issued.EmailCode.Should().BeNull("the challenge and its ceremony row are written: the sign-in must not fail now");
    }

    // ── T2, V3: the response member ──

    // Exactly what the API writes with (Program.cs applies the same method).
    private static readonly JsonSerializerOptions ApiJson = ApiJsonFor();

    private static JsonSerializerOptions ApiJsonFor()
    {
        var options = new JsonSerializerOptions();
        Auth_API.Common.ApiJsonOptions.Apply(options);
        return options;
    }

    [Fact]
    public void ThePendingLogin_CarriesWhereTheCodeWent_WhenOneWasSent()
    {
        var response = new IssuedTwoFactorChallenge("token", [TwoFactorMethodNames.Email, TwoFactorMethodNames.Recovery], Sent)
            .ToPendingLogin();

        response.RequiresTwoFactor.Should().BeTrue();
        response.TwoFactorChallengeToken.Should().Be("token");
        response.TwoFactorEmailCode.Should().Be(new SignInEmailCodeResponse(Sent.ExpiresAt, Sent.MaskedEmail, Sent.SendsRemaining));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, ApiJson));
        var member = json.RootElement.GetProperty("twoFactorEmailCode");
        member.GetProperty("maskedEmail").GetString().Should().Be("p****n@example.com");
        member.GetProperty("sendsRemaining").GetInt32().Should().Be(2);
        member.TryGetProperty("expiresAt", out _).Should().BeTrue();
    }

    [Fact]
    public void ThePendingLogin_WritesNoCodeMember_WhenNoneWasSent()
    {
        var response = new IssuedTwoFactorChallenge("token", [TwoFactorMethodNames.Totp, TwoFactorMethodNames.Recovery])
            .ToPendingLogin();

        response.TwoFactorEmailCode.Should().BeNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, ApiJson));
        json.RootElement.TryGetProperty("twoFactorEmailCode", out _).Should().BeFalse(
            "a client built before D-243 reads the body it always read");
        json.RootElement.GetProperty("twoFactorMethods").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void TheIssuedChallenge_NeverPrintsItsToken()
    {
        new IssuedTwoFactorChallenge("secret-token", null, Sent).ToString().Should().NotContain("secret-token");
    }

    // ── T5, V5: the provider that controls the account's own mailbox ──

    private static ExternalUserInfo Provider(string email, bool controlsMailbox = true) =>
        new("sub", email, "First", "Last", null, null, EmailVerified: true) { ControlsMailbox = controlsMailbox };

    [Fact]
    public async Task AProviderControllingTheAccountsOwnMailbox_ProvesTheEmailedCode()
    {
        GivenRow(EmailRow(_user.Id));

        (await Service().ProviderProvesMailboxAsync(_user, Provider(" Person@Example.com "), CancellationToken.None))
            .Should().BeTrue("the address is normalized as every address is: trimmed, lower case");
    }

    [Theory]
    [InlineData("another address", "other@example.com", true, "email")]
    [InlineData("a dotted form of it", "per.son@example.com", true, "email")]
    [InlineData("a plus form of it", "person+x@example.com", true, "email")]
    [InlineData("a provider that only checked it once", AccountAddress, false, "email")]
    [InlineData("an authenticator-app account", AccountAddress, true, "totp")]
    [InlineData("a row holding both factors", AccountAddress, true, "both")]
    [InlineData("no factor row", AccountAddress, true, "none")]
    [InlineData("an address that is no address", "not-an-address", true, "email")]
    public async Task AnyOtherProviderSignIn_ProvesNothing(string because, string address, bool controlsMailbox, string row)
    {
        GivenRow(row switch
        {
            "email" => EmailRow(_user.Id),
            "totp" => TotpRow(_user.Id),
            "both" => BothFactorsRow(_user.Id),
            _ => null
        });

        (await Service().ProviderProvesMailboxAsync(_user, Provider(address, controlsMailbox), CancellationToken.None))
            .Should().BeFalse(because);
    }

    // ── T6, V6: which first factors the emailed code follows ──

    [Fact]
    public void TheEmailedCode_FollowsThePasswordOrAProvider_AtSignIn_AndThePasswordAlone_AtRecovery()
    {
        EmailCodeProofStrategy.MayFollowAtSignIn(AuthenticationMethods.Password).Should().BeTrue();
        EmailCodeProofStrategy.MayFollowAtSignIn(AuthenticationMethods.ExternalIdentity).Should().BeTrue();
        EmailCodeProofStrategy.MayFollowAtSignIn(AuthenticationMethods.EmailCode).Should().BeFalse();
        EmailCodeProofStrategy.MayFollowAtSignIn(AuthenticationMethods.Unknown).Should().BeFalse();

        // The deletion recovery's provider entry has no way to send a code (OI-243 deviation).
        EmailCodeProofStrategy.MayFollow(AuthenticationMethods.Password).Should().BeTrue();
        EmailCodeProofStrategy.MayFollow(AuthenticationMethods.ExternalIdentity).Should().BeFalse();
    }

    // ── V7: what either provider session carries ──

    [Fact]
    public void AProviderSession_WithOrWithoutTheEmailedCode_CarriesNoAmr_AndSatisfiesNoPolicy()
    {
        var waived = AuthenticationMethods.ExternalIdentity;
        var withCode = AuthenticationMethods.ExternalIdentity.With(AuthenticationMethods.EmailCode);

        waived.ToAmrValues().Should().BeEmpty();
        withCode.ToAmrValues().Should().BeEmpty();
        waived.IsMfaSatisfied.Should().BeFalse();
        withCode.IsMfaSatisfied.Should().BeFalse();
    }
}
