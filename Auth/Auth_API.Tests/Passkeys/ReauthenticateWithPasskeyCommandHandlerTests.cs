using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Passkeys.ReauthenticateWithPasskey;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// A signed-in person proving themself again with a passkey, with no new sign-in (owner decision,
/// 2026-11-09), with the real WebAuthn library checking a software authenticator's answers. The
/// options name no credential, so the session is what the answer is bound to: a passkey of another
/// account proves nothing here, though its signature is good.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the comparison with the session's owner dropped (another
/// account's passkey stamps this session); the session checked after the ceremony (a dead token
/// spends a challenge). REVIEW-FIXES 152: the event given no address (F25); another account's
/// passkey refused after its use is recorded (NIT-5); its line logged as a failed verification
/// (R4); the publish moved above the outcome checks (F28: the "no audit event" checks, which could
/// not fail before).
/// </remarks>
public class ReauthenticateWithPasskeyCommandHandlerTests
{
    private const string IdpCookie = "idp-cookie";
    private const string Ip = "203.0.113.7";
    private const string Agent = "Agent/1.0";

    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly AuthenticationMethods PasskeyProof =
        AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);

    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly FakeCredentialHasher _keys = new();

    public ReauthenticateWithPasskeyCommandHandlerTests()
    {
        _keys.Map(IdpCookie, "idp-hash");
        _store.Setup(s => s.TryCommitSessionProofAsync(
                It.IsAny<Guid>(), It.IsAny<SessionProofCommit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);
    }

    private ReauthenticateWithPasskeyCommandHandler Handler(PasskeySignInFixture fixture) =>
        new(
            new ReauthenticationGuard(
                _sessions.Object,
                TestHelpers.CreateOptions(new TwoFactorSettings()),
                TestHelpers.AccessToken(DateTime.UtcNow),
                TimeProvider.System,
                NullLogger<ReauthenticationGuard>.Instance),
            fixture.Ceremony(),
            fixture.Users.Object,
            _store.Object,
            _keys,
            _publisher.Object,
            TimeProvider.System,
            NullLogger<ReauthenticateWithPasskeyCommandHandler>.Instance);

    private void GivenSessionOf(Guid userId) =>
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(id: SessionId, userId: userId));

    private static ReauthenticateWithPasskeyCommand Command(Guid userId, string responseJson, string? handle = PasskeySignInFixture.Handle) =>
        new(userId, SessionId, IdpCookie, handle, false, responseJson, Ip, Agent);

    private void VerifyNothingStamped() =>
        _store.Verify(s => s.TryCommitSessionProofAsync(
            It.IsAny<Guid>(), It.IsAny<SessionProofCommit>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task AVerifiedPasskey_StampsTheSession_WithTwoFactors_AndWritesTheAuditEvent()
    {
        using var fixture = new PasskeySignInFixture();
        GivenSessionOf(fixture.Owner.Id);

        var result = await Handler(fixture).Handle(Command(fixture.Owner.Id, await fixture.AnswerAsync()), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        PasskeyProof.IsMfaSatisfied.Should().BeTrue();
        _store.Verify(s => s.TryCommitSessionProofAsync(
            fixture.Owner.Id,
            It.Is<SessionProofCommit>(commit =>
                commit.Session.SessionId == SessionId
                && commit.Session.Method == PasskeyProof
                && commit.Session.IdpHashes != null
                && commit.SecondFactor == null
                && commit.FirstProofCode == null),
            CancellationToken.None), Times.Once, "its challenge is spent: a client that leaves must not lose the proof");
        _publisher.Verify(p => p.Publish(
            It.Is<UserReauthenticatedEvent>(e => e.UserId == fixture.Owner.Id && e.SessionId == SessionId && e.Methods == PasskeyProof
                && e.IpAddress == Ip && e.UserAgent == Agent),
            CancellationToken.None), Times.Once);
        fixture.VerifyNoSession();
    }

    [Fact]
    public async Task APasskeyOfAnotherAccount_StampsNothing_AndSaysSo_ThoughItsSignatureIsGood()
    {
        using var fixture = new PasskeySignInFixture();
        var signedIn = Guid.NewGuid();
        GivenSessionOf(signedIn);

        var result = await Handler(fixture).Handle(Command(signedIn, await fixture.AnswerAsync()), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.NotThisAccount.Code);
        VerifyNothingStamped();
        _publisher.Verify(p => p.Publish(It.IsAny<UserReauthenticatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);

        // NIT-5: nothing of that passkey is recorded, so its account's list shows no use; the
        // challenge is spent all the same.
        fixture.Passkeys.Verify(p => p.TryRecordAssertionAsync(
            It.IsAny<Auth.Domain.Entities.UserPasskey>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.ConsumeCalls.Should().Be(1);

        // R4: its own line, which never inflates the count of failed verifications.
        fixture.CeremonyLog.Entries.Should().ContainSingle(entry => entry.Message.StartsWith(Auth.Domain.Constants.PasskeySignalCodes.NotThisAccount))
            .Which.Values.Should().Contain(signedIn.ToString());
        fixture.CeremonyLog.Entries.Should().NotContain(entry => entry.Message.StartsWith(Auth.Domain.Constants.PasskeySignalCodes.VerificationFailed));
    }

    [Fact]
    public async Task ADeadSession_SpendsNoChallenge()
    {
        using var fixture = new PasskeySignInFixture();
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: SessionId, userId: fixture.Owner.Id, isActive: false, terminatedAt: DateTime.UtcNow));

        var result = await Handler(fixture).Handle(Command(fixture.Owner.Id, await fixture.AnswerAsync()), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        fixture.ConsumeCalls.Should().Be(0);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task ANoCookieAnswer_IsAFailedCeremony()
    {
        using var fixture = new PasskeySignInFixture();
        GivenSessionOf(fixture.Owner.Id);

        var result = await Handler(fixture).Handle(
            Command(fixture.Owner.Id, await fixture.AnswerAsync(), handle: null), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task ACorruptSignature_IsAFailedCeremony_WithItsChallengeSpent()
    {
        using var fixture = new PasskeySignInFixture();
        GivenSessionOf(fixture.Owner.Id);

        var result = await Handler(fixture).Handle(
            Command(fixture.Owner.Id, await fixture.AnswerAsync(new CeremonyTweaks { CorruptSignature = true })),
            CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.ConsumeCalls.Should().Be(1);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task AnAdministratorsLock_Refuses_ALockOfWrongPasswordsAdmits()
    {
        using (var locked = new PasskeySignInFixture(status: UserStatus.Locked))
        {
            GivenSessionOf(locked.Owner.Id);

            var refused = await Handler(locked).Handle(Command(locked.Owner.Id, await locked.AnswerAsync()), CancellationToken.None);

            refused.IsError.Should().BeTrue();
            VerifyNothingStamped();
        }

        using var wrongPasswords = new PasskeySignInFixture(failedLoginAttempts: 5, lockoutEnd: DateTime.UtcNow.AddMinutes(10));
        GivenSessionOf(wrongPasswords.Owner.Id);

        var admitted = await Handler(wrongPasswords).Handle(
            Command(wrongPasswords.Owner.Id, await wrongPasswords.AnswerAsync()), CancellationToken.None);

        admitted.IsError.Should().BeFalse("a stranger's wrong passwords must not stop the one way a look-alike site cannot relay");
    }

    [Fact]
    public async Task ASessionThatEndedBeforeTheCommit_AsksToSignIn_AndWritesNoAuditEvent()
    {
        using var fixture = new PasskeySignInFixture();
        GivenSessionOf(fixture.Owner.Id);
        _store.Setup(s => s.TryCommitSessionProofAsync(
                It.IsAny<Guid>(), It.IsAny<SessionProofCommit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.SessionLost);

        var result = await Handler(fixture).Handle(Command(fixture.Owner.Id, await fixture.AnswerAsync()), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _publisher.Verify(p => p.Publish(It.IsAny<UserReauthenticatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
