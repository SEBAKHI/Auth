using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.AccountDeletion.RecoverAccountWithPasskey;
using Auth.Application.Features.Passkeys.BeginPasskeyAssertion;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.VerifyPasskeyAssertion;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication.Passkeys;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// A passkey sign-in end to end below HTTP (S20 PR B): the REAL WebAuthn adapter over a software
/// authenticator the test owns, so every signature and every check is real; the stores are mocks
/// that behave as the tables do for one passkey: the challenge is spent once, the row is found by
/// its exact credential id, and the record is conditional on the counter it was read with.
/// </summary>
internal sealed class PasskeySignInFixture : IDisposable
{
    public const string Origin = PasskeyTestDoubles.AccountsOrigin;
    public const string Handle = "assertion-handle";

    public static readonly byte[] OwnerHandle = Enumerable.Range(1, 64).Select(i => (byte)i).ToArray();

    private readonly List<string> _issuedOptions = [];
    private int _consumed;

    /// <param name="algorithm">The authenticator's algorithm.</param>
    /// <param name="storedSignCount">The counter the stored passkey last accepted.</param>
    /// <param name="credentialId">The credential id; random by default.</param>
    /// <param name="storedPublicKey">The stored key, when it is not the authenticator's own.</param>
    /// <param name="status">The owner's status.</param>
    /// <param name="failedLoginAttempts">The owner's failure count.</param>
    /// <param name="lockoutEnd">The owner's lock expiry.</param>
    public PasskeySignInFixture(
        int algorithm = SoftwareAuthenticator.Es256,
        long storedSignCount = 0,
        byte[]? credentialId = null,
        byte[]? storedPublicKey = null,
        UserStatus status = UserStatus.Active,
        int failedLoginAttempts = 0,
        DateTime? lockoutEnd = null)
    {
        Authenticator = SoftwareAuthenticator.Create(algorithm, credentialId);
        Owner = TestHelpers.CreateUser(
            email: "owner@example.com", status: status, failedLoginAttempts: failedLoginAttempts, lockoutEnd: lockoutEnd);
        Passkey = new UserPasskey(
            Guid.NewGuid(), Owner.Id, Authenticator.CredentialId, storedPublicKey ?? Authenticator.CosePublicKey(), storedSignCount,
            PasskeyTestDoubles.RpId, isUserVerified: true, isBackupEligible: false, isBackedUp: false, "Laptop",
            DateTime.UtcNow.AddDays(-30), lastUsedAt: null, lastCounterRegressionAt: null, revokedAt: null,
            revokedReason: null, revokedBy: null);

        // The challenge store: one consume per issued challenge; a second finds nothing.
        Challenges.Setup(c => c.CreateAsync(It.IsAny<WebAuthnChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<WebAuthnChallenge, CancellationToken>((challenge, _) => _issuedOptions.Add(challenge.OptionsJson))
            .Returns(Task.CompletedTask);
        Challenges.Setup(c => c.TryConsumeAssertionAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _consumed++ == 0 && _issuedOptions.Count > 0 ? _issuedOptions[^1] : null);

        // The passkey store, as the table holds one passkey: found by its exact bytes.
        Passkeys.Setup(p => p.GetLiveByCredentialIdAsync(It.IsAny<byte[]>(), PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] id, string _, CancellationToken _) =>
                PasskeyLive && id.AsSpan().SequenceEqual(Passkey.CredentialId) ? Passkey : null);
        Passkeys.Setup(p => p.GetUserHandleAsync(Owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => StoredOwnerHandle);
        Passkeys.Setup(p => p.TryRecordAssertionAsync(It.IsAny<UserPasskey>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => RecordSucceeds);
        // The read after the session (REVIEW 151 F2): live unless a test revokes it meanwhile.
        Passkeys.Setup(p => p.GetLiveByIdAsync(Owner.Id, Passkey.Id, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PasskeyLiveAfterTheSession ? Passkey : null);

        Users.Setup(u => u.GetByIdAsync(Owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => OwnerDeleted ? null : Owner);
        Users.Setup(u => u.GetByIdIncludeDeletedAsync(Owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => DeletedOwner ?? Owner);

        Builder.Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AuthenticationMethods>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .ReturnsAsync(new LoginResponse { User = new UserInfo { Id = Owner.Id, Email = "owner@example.com", FirstName = "Test", LastName = "User" } });
    }

    public SoftwareAuthenticator Authenticator { get; }

    public User Owner { get; }

    public UserPasskey Passkey { get; }

    /// <summary>The verifier: the real adapter unless a test replaces it.</summary>
    public IWebAuthnVerifier Verifier { get; set; } = new Fido2WebAuthnVerifier(NullLogger<Fido2WebAuthnVerifier>.Instance);

    public Mock<IWebAuthnChallengeRepository> Challenges { get; } = new();

    public Mock<IUserPasskeyRepository> Passkeys { get; } = new();

    public Mock<IUserRepository> Users { get; } = new();

    public Mock<IAccountDeletionRequestRepository> Deletions { get; } = new();

    public Mock<ILoginResponseBuilder> Builder { get; } = new();

    public Mock<IDomainEventDispatcher> Events { get; } = new();

    public CapturingLogger<PasskeyAssertionCeremony> CeremonyLog { get; } = new();

    public CapturingLogger<VerifyPasskeyAssertionCommandHandler> VerifyLog { get; } = new();

    public CapturingLogger<RecoverAccountWithPasskeyCommandHandler> RecoverLog { get; } = new();

    /// <summary>Whether the passkey is live (a removal makes it vanish from the live lookup).</summary>
    public bool PasskeyLive { get; set; } = true;

    /// <summary>The owner's stored handle.</summary>
    public byte[]? StoredOwnerHandle { get; set; } = OwnerHandle;

    /// <summary>Whether the conditional record matches its row.</summary>
    public bool RecordSucceeds { get; set; } = true;

    /// <summary>Whether the passkey is still live when it is read again after the session is written.</summary>
    public bool PasskeyLiveAfterTheSession { get; set; } = true;

    /// <summary>What ends a session the sign-in issued with a passkey revoked meanwhile.</summary>
    public Mock<ICredentialRevocationService> Revocation { get; } = new();

    /// <summary>Whether the sign-in's read of the owner (deleted accounts excluded) finds nothing.</summary>
    public bool OwnerDeleted { get; set; }

    /// <summary>The owner as a read that includes deleted accounts finds it, when it differs.</summary>
    public User? DeletedOwner { get; set; }

    /// <summary>The passkey feature: switched on over the example RP ID and origins.</summary>
    public PasskeyFeature Feature { get; set; } = PasskeyTestDoubles.Feature();

    /// <summary>How many times the challenge store was asked to spend a challenge.</summary>
    public int ConsumeCalls => _consumed;

    public PasskeyAssertionCeremony Ceremony() =>
        new(Feature, Challenges.Object, Passkeys.Object, Verifier, new FakeCredentialHasher(), Revocation.Object, TimeProvider.System, CeremonyLog);

    public BeginPasskeyAssertionCommandHandler Begin() =>
        new(Feature, Challenges.Object, Verifier, Mock.Of<ISecureTokenGenerator>(t => t.Generate() == Handle), new FakeCredentialHasher(), TimeProvider.System);

    public VerifyPasskeyAssertionCommandHandler Verify() =>
        new(Ceremony(), Users.Object, Deletions.Object, Builder.Object, Events.Object, TimeProvider.System, VerifyLog);

    public RecoverAccountWithPasskeyCommandHandler Recover(AccountDeletionRecoverer recoverer) =>
        new(Ceremony(), Users.Object, Deletions.Object, recoverer, TimeProvider.System, RecoverLog);

    /// <summary>Starts a sign-in and returns the options the browser receives.</summary>
    public async Task<string> OptionsAsync()
    {
        var started = await Begin().Handle(new BeginPasskeyAssertionCommand(null, false), CancellationToken.None);
        started.IsError.Should().BeFalse(started.IsError ? started.FirstError.Code : "");
        return started.Value.OptionsJson;
    }

    /// <summary>The authenticator's answer to fresh options, from the accounts app.</summary>
    public async Task<string> AnswerAsync(CeremonyTweaks? tweaks = null, string origin = Origin, byte[]? userHandle = null) =>
        Authenticator.Assert(await OptionsAsync(), origin, userHandle ?? OwnerHandle, tweaks);

    public VerifyPasskeyAssertionCommand VerifyCommand(string responseJson, string? handle = Handle, bool repeated = false, bool staySignedIn = false) =>
        new(handle, repeated, responseJson, "203.0.113.7", "Agent/1.0", "device-1", staySignedIn);

    public RecoverAccountWithPasskeyCommand RecoverCommand(string responseJson, string? handle = Handle, bool repeated = false) =>
        new(handle, repeated, responseJson, "203.0.113.7", "Agent/1.0", "device-1");

    public async Task<ErrorOr<LoginResponse>> SignInAsync(CeremonyTweaks? tweaks = null, bool staySignedIn = false) =>
        await Verify().Handle(VerifyCommand(await AnswerAsync(tweaks), staySignedIn: staySignedIn), CancellationToken.None);

    public void VerifyNoSession() =>
        Builder.Verify(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AuthenticationMethods>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()),
            Times.Never);

    public void VerifySession(bool persistent = false) =>
        Builder.Verify(b => b.BuildAsync(
                It.Is<User>(user => user.Id == Owner.Id), "203.0.113.7", "Agent/1.0", "device-1",
                AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified),
                false, It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), null,
                It.IsAny<string?>(), null, persistent, It.IsAny<Guid?>(), It.IsAny<bool>()),
            Times.Once);

    public void Dispose() => Authenticator.Dispose();
}
