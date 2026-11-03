using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Features.Secrets.DiscardStagedSigningKey;
using Auth.Application.Features.Secrets.GetHmacKeyring;
using Auth.Application.Features.Secrets.PromoteSigningKey;
using Auth.Application.Features.Secrets.RequestSecretOperationChallenge;
using Auth.Application.Features.Secrets.RetireSigningKey;
using Auth.Application.Features.Secrets.StageSigningKey;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth_API.Common.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S12 TS5, TS6 (KR3): every signing keyring change is confirmed with the RSA step-up, bound to
/// its action and the key it applies to; a scheduled promote needs the staged key loaded here and
/// published for 24 hours, an emergency needs neither; a promote or a retire takes the demoted key
/// out of the file and its backup; and a change reaches this process only over keys it loaded.
/// Over a real secrets file; <see cref="KeyringTestHarness.RestartAsync"/> is an API recycle.
/// </summary>
public sealed class RsaKeyringCommandHandlerTests : IDisposable
{
    private readonly KeyringTestHarness _keyring = new();

    public RsaKeyringCommandHandlerTests()
    {
        // Stand the clock still, so windows are exact.
        _keyring.Clock.Advance(TimeSpan.Zero);
    }

    public void Dispose() => _keyring.Dispose();

    // --- promote ---

    [Fact]
    public async Task APromote_IsRefusedWhileTheStagedKeyIsNotLoadedHere_WithoutSpendingTheApproval()
    {
        await StageAsync();
        _keyring.Clock.Advance(TimeSpan.FromDays(2));
        var approval = _keyring.ApproveSigning(KeyringAction.Promote);

        var result = await Promote(approval);

        result.FirstError.Code.Should().Be("Secret.KeyringNoNextKey", "the restart that loads the staged key has not happened");
        result.FirstError.Description.Should().Contain("restart the API");
        NotSpent(approval);
    }

    [Fact]
    public async Task APromote_FromAProcessThatNeverLoadedTheStagedKey_IsRefused_ThoughAnotherPublishedIt()
    {
        // Two API processes share the file: the one that restarted loaded and published the
        // staged key; this one started before the stage. Promoting here would move the file to a
        // key this process cannot sign with.
        var stale = new Auth.Application.Features.Secrets.Common.RsaKeyringChangeService(
            _keyring.Secrets,
            _keyring.SigningKeys,
            _keyring.Challenges.Service,
            _keyring.Publisher.Object,
            new TestHelpers.TestOptions<JwtSettings>(_keyring.Jwt),
            _keyring.Clock,
            new Mock<ILogger<Auth.Application.Features.Secrets.Common.RsaKeyringChangeService>>().Object);
        await StagePublishAndWaitAsync();
        var approval = _keyring.ApproveSigning(KeyringAction.Promote);

        var result = await new PromoteSigningKeyCommandHandler(stale).Handle(
            new PromoteSigningKeyCommand(false, approval, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.KeyringNoNextKey");
        NotSpent(approval);
    }

    [Fact]
    public async Task APromote_IsRefusedBefore24HoursOfPublication_NamingTheTime()
    {
        await StageAsync();
        await _keyring.RestartAsync();
        var publishedAt = _keyring.ReadSigningRing().Next!.PublishedAtUtc!.Value;
        _keyring.Clock.Advance(TimeSpan.FromHours(23));
        var approval = _keyring.ApproveSigning(KeyringAction.Promote);

        var early = await Promote(approval);

        early.FirstError.Code.Should().Be("Secret.KeyringNextTooYoung");
        early.FirstError.Description.Should().Contain(publishedAt.AddHours(24).ToString("u"));
        NotSpent(approval);

        _keyring.Clock.Advance(TimeSpan.FromHours(1));
        (await Promote(_keyring.ApproveSigning(KeyringAction.Promote))).IsError.Should().BeFalse("24 hours have passed");
    }

    [Fact]
    public async Task AScheduledPromote_MovesTheSignerHere_AndTheOldKeyIsAcceptedForADayAndFiveMinutes()
    {
        var old = _keyring.SigningKeys.Current.Key.KeyId!;
        var token = SignedNow();
        var staged = await StagePublishAndWaitAsync();

        var result = await Promote(_keyring.ApproveSigning(KeyringAction.Promote));

        result.IsError.Should().BeFalse(Codes(result));
        result.Value.ChangeAppliedInProcess.Should().BeTrue();
        _keyring.SigningKeys.Current.Key.KeyId.Should().Be(staged, "the window starts when the old key stops signing, so the signer moves now");
        var previous = _keyring.ReadSigningRing().Find(old)!;
        previous.VerifyUntilUtc.Should().Be(_keyring.Clock.UtcNow.AddMinutes(1440).AddSeconds(300),
            "max(1440 min, a 15-minute lifetime) + 300 s (T12)");
        previous.PrivateKeyPem.Should().BeNull();
        Accepted(token).Should().BeTrue("a session from before the promote continues: its token is still accepted");
        _keyring.ReadSigningRing().Persisted.Should().BeTrue();
    }

    [Fact]
    public async Task APromote_TakesTheDemotedPrivateKeyOutOfTheFileAndItsBackup()
    {
        var oldPrivate = _keyring.ReadSigningRing().Current.PrivateKeyPem!;
        await StagePublishAndWaitAsync();
        HoldsPrivateKey(_keyring.FilePath + ".bak", oldPrivate).Should().BeTrue("the backup holds the key while it is current");

        (await Promote(_keyring.ApproveSigning(KeyringAction.Promote))).IsError.Should().BeFalse();

        HoldsPrivateKey(_keyring.FilePath, oldPrivate).Should().BeFalse();
        HoldsPrivateKey(_keyring.FilePath + ".bak", oldPrivate).Should().BeFalse(
            "the second save overwrites the backup, which held the key while it was current (SP 800-57 §5.3.6)");
    }

    // --- emergency ---

    [Fact]
    public async Task AnEmergency_SkipsBothRefusals_AndWithoutTheStagedKeyLoaded_ClosesTheLeakedKeyAtTheRecycle()
    {
        var leaked = _keyring.SigningKeys.Current.Key.KeyId!;
        var token = SignedNow();
        await StageAsync();

        var result = await Promote(_keyring.ApproveSigning(KeyringAction.PromoteEmergency), emergency: true);

        result.IsError.Should().BeFalse(Codes(result));
        result.Value.ChangeAppliedInProcess.Should().BeFalse("this process never loaded the staged key: it says so (PB2)");
        _keyring.ReadSigningRing().Find(leaked)!.Compromised.Should().BeTrue();
        _keyring.SigningKeys.Current.Key.KeyId.Should().Be(leaked, "it signs with the old key until it restarts");
        result.Value.Keys.Single(key => key.Kid == leaked).AcceptedInProcess.Should().BeTrue(
            "the answer shows the leaked key still accepted here, never closed while it is not");

        await _keyring.RestartAsync();
        Accepted(token).Should().BeFalse("the recycle closes it");
    }

    [Fact]
    public async Task AnEmergency_WithTheStagedKeyLoaded_ClosesTheLeakedKeyHereAtOnce()
    {
        var leaked = _keyring.SigningKeys.Current.Key.KeyId!;
        var token = SignedNow();
        var staged = await StageAsync();
        await _keyring.RestartAsync();

        var result = await Promote(_keyring.ApproveSigning(KeyringAction.PromoteEmergency), emergency: true);

        result.IsError.Should().BeFalse(Codes(result));
        result.Value.ChangeAppliedInProcess.Should().BeTrue();
        _keyring.SigningKeys.Current.Key.KeyId.Should().Be(staged);
        Accepted(token).Should().BeFalse("a leaked key is neither accepted nor published from this moment");
        _keyring.SigningKeys.PublishedKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().NotContain(leaked);
        _keyring.ReadSigningRing().Find(leaked)!.PrivateKeyPem.Should().BeNull("a compromised record keeps its public key only (KR1)");
    }

    [Fact]
    public async Task APromoteApproval_IsNeverSpentOnAnEmergency_NorTheReverse()
    {
        await StagePublishAndWaitAsync();
        var scheduled = _keyring.ApproveSigning(KeyringAction.Promote);
        var emergency = _keyring.ApproveSigning(KeyringAction.PromoteEmergency);

        (await Promote(scheduled, emergency: true)).FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        (await Promote(emergency)).FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
    }

    [Fact]
    public async Task AnHmacApproval_IsNeverSpentOnTheSigningKeyring()
    {
        // The rings' digests have different prefixes (keyring:hmac/ and keyring:rsa/).
        await StagePublishAndWaitAsync();
        var hmac = Guid.NewGuid();
        var target = KeyringStepUp.SigningTargetOf(KeyringAction.Promote, _keyring.ReadSigningRing(), null, null);
        _keyring.Challenges.WithApproval(hmac, SecretOperation.GenerateRsaKey, _keyring.Admin, KeyringStepUp.Digest(KeyringAction.Promote, target, null));

        (await Promote(hmac)).FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
    }

    // --- stage, discard ---

    [Fact]
    public async Task AStage_ChangesTheFileOnly_AndTheRestartLoadsAndPublishesTheKey()
    {
        var staged = await StageAsync();

        _keyring.SigningKeys.PublishedKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().NotContain(staged,
            "a stage loads nothing into the running process (PB1)");
        (await View()).Keys.Single(key => key.Kid == staged).LoadedInProcess.Should().BeFalse();

        (await _keyring.RestartAsync()).Should().Be(SigningKeyPublication.Outcome.Recorded);

        _keyring.SigningKeys.PublishedKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().Contain(staged);
        _keyring.ReadSigningRing().Next!.PublishedAtUtc.Should().Be(_keyring.Clock.UtcNow);
        (await View()).PromoteAvailableAtUtc.Should().Be(_keyring.Clock.UtcNow.AddHours(24));
    }

    [Fact]
    public async Task ADiscardThenAStageWithoutARecycle_LeavesNothingToPromote()
    {
        await StageAsync();
        await _keyring.RestartAsync();
        (await Discard()).IsError.Should().BeFalse();
        await StageAsync();
        _keyring.Clock.Advance(TimeSpan.FromHours(25));

        (await Promote(_keyring.ApproveSigning(KeyringAction.Promote))).FirstError.Code.Should().Be("Secret.KeyringNoNextKey",
            "the key staged after the discard is not the one this process loaded");
    }

    [Fact]
    public async Task StageImport_OfTheCurrentKey_OrOfACompromisedOrDiscardedOne_IsRefused()
    {
        var current = _keyring.ReadSigningRing().Current.PrivateKeyPem!;
        (await StageImport(current)).FirstError.Code.Should().Be("Secret.KeyringKeyReferenced");

        var discarded = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateRsaKeyPair().PrivateKeyPem;
        (await StageImport(discarded)).IsError.Should().BeFalse();
        (await Discard()).IsError.Should().BeFalse();
        var approval = _keyring.ApproveSigning(KeyringAction.StageImport, value: discarded);
        var again = await new StageSigningKeyCommandHandler(_keyring.SigningService, new SharedKeyMaterialGenerator())
            .Handle(new StageSigningKeyCommand(discarded, approval, _keyring.Admin), CancellationToken.None);
        again.FirstError.Code.Should().Be("Secret.KeyringKeyReferenced", "a key that left the ring is never staged again (T6)");
        NotSpent(approval);

        await StageAsync();
        await _keyring.RestartAsync();
        (await Promote(_keyring.ApproveSigning(KeyringAction.PromoteEmergency), emergency: true)).IsError.Should().BeFalse();
        (await StageImport(current)).FirstError.Code.Should().Be("Secret.KeyringKeyReferenced", "a compromised key is never staged again");
    }

    // --- retire ---

    [Fact]
    public async Task ARetire_RefusesTheCurrentAndStagedKeys_AndRemovesAPreviousKeyFromTheFileAndThisProcess()
    {
        var old = _keyring.SigningKeys.Current.Key.KeyId!;
        var staged = await StagePublishAndWaitAsync();
        (await Retire(staged)).FirstError.Code.Should().Be("Secret.KeyringKeyReferenced");
        (await Retire("no-such-kid")).FirstError.Code.Should().Be("Secret.KeyringKeyNotFound");
        (await Promote(_keyring.ApproveSigning(KeyringAction.Promote))).IsError.Should().BeFalse();
        (await Retire(staged)).FirstError.Code.Should().Be("Secret.KeyringKeyReferenced", "it is the current key now");

        var retired = await Retire(old);

        retired.IsError.Should().BeFalse(Codes(retired));
        retired.Value.ChangeAppliedInProcess.Should().BeTrue();
        _keyring.ReadSigningRing().Find(old).Should().BeNull();
        _keyring.ReadSigningRing().Removed.Should().ContainSingle(key => key.Reason == SigningKeyRemoval.Retired);
        _keyring.SigningKeys.VerificationKeys(DateTimeOffset.UtcNow).Select(key => key.KeyId).Should().NotContain(old);
    }

    [Fact]
    public async Task ARetire_OfAKeyThisProcessStillSignsWith_IsRefused_ThoughAnotherProcessDemotedIt()
    {
        // Two API processes share the file: the one that restarted promoted; this one started
        // before the stage, was never recycled, and still signs with the key the promote demoted.
        // Retiring it from here would take out of the file the key this process signs with.
        var old = _keyring.SigningKeys.Current.Key.KeyId!;
        var stale = new Auth.Application.Features.Secrets.Common.RsaKeyringChangeService(
            _keyring.Secrets,
            _keyring.SigningKeys,
            _keyring.Challenges.Service,
            _keyring.Publisher.Object,
            new TestHelpers.TestOptions<JwtSettings>(_keyring.Jwt),
            _keyring.Clock,
            new Mock<ILogger<Auth.Application.Features.Secrets.Common.RsaKeyringChangeService>>().Object);
        await StagePublishAndWaitAsync();
        (await Promote(_keyring.ApproveSigning(KeyringAction.Promote))).IsError.Should().BeFalse();
        var approval = _keyring.ApproveSigning(KeyringAction.Retire, kid: old);

        var result = await new RetireSigningKeyCommandHandler(stale).Handle(
            new RetireSigningKeyCommand(old, approval, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.KeyringKeyReferenced");
        NotSpent(approval);
        _keyring.ReadSigningRing().Find(old).Should().NotBeNull("the file keeps the key a running process signs with");
        (await Retire(old)).IsError.Should().BeFalse("the process that promoted no longer signs with it");
    }

    // --- where the ring cannot be kept ---

    [Fact]
    public async Task PlainTextMode_RefusesEveryChange_AndTheReadSaysWhy()
    {
        using var plain = new KeyringTestHarness(plainText: true);

        var staged = await new StageSigningKeyCommandHandler(plain.SigningService, new SharedKeyMaterialGenerator())
            .Handle(new StageSigningKeyCommand(null, Guid.NewGuid(), plain.Admin), CancellationToken.None);
        var view = await plain.SigningService.ViewAsync(CancellationToken.None);

        staged.FirstError.Code.Should().Be("Secret.KeyringUnavailable");
        view.Available.Should().BeFalse();
        view.UnavailableReason.Should().Be("PlainText");
    }

    [Fact]
    public async Task AChallengeForASigningKeyringChange_IsBoundToItsRing_AndRefusedBeforeACodeIsEmailed()
    {
        var request = new RequestSecretOperationChallengeCommandHandler(
            _keyring.Challenges.Service,
            _keyring.Approvals,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = "Certificate" }),
            _keyring.Publisher.Object,
            new Mock<ILogger<RequestSecretOperationChallengeCommandHandler>>().Object);

        var promote = await request.Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateRsaKey, null, _keyring.Admin, null, KeyringAction.Promote),
            CancellationToken.None);
        var stage = await request.Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateRsaKey, null, _keyring.Admin, null, KeyringAction.Stage),
            CancellationToken.None);

        promote.FirstError.Code.Should().Be("Secret.KeyringNoNextKey", "nothing is staged: no code is emailed for it");
        stage.IsError.Should().BeFalse(Codes(stage));
    }

    // --- the read ---

    [Fact]
    public async Task TheKeyringRead_CarriesTheSigningRing_WithoutKeyMaterial()
    {
        await StageAsync();
        var ring = _keyring.ReadSigningRing();

        var read = await new GetHmacKeyringQueryHandler(_keyring.Service, _keyring.SigningService)
            .Handle(new GetHmacKeyringQuery(), CancellationToken.None);

        read.IsError.Should().BeFalse();
        var json = JsonSerializer.Serialize(read.Value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        foreach (var key in ring.Keys)
        {
            json.Should().NotContain(Body(key.PrivateKeyPem ?? key.PublicKeyPem!), $"key {key.Kid}'s material must never leave the server");
        }

        read.Value.Rsa!.Keys.Select(key => (key.State, key.LoadedInProcess))
            .Should().Equal((SigningKeyState.Current, true), (SigningKeyState.Next, false));
        read.Value.Rsa.InSync.Should().BeFalse("the staged key waits for the restart");
    }

    [Fact]
    public async Task EveryChange_IsAuditedWithItsKid()
    {
        var staged = await StageAsync();

        _keyring.Publisher.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e =>
                    e.Operation == SecretOperation.GenerateRsaKey && e.Action == KeyringAction.Stage && e.KeyId == staged && e.Target == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // --- helpers ---

    private async Task<string> StageAsync()
    {
        var staged = await new StageSigningKeyCommandHandler(_keyring.SigningService, new SharedKeyMaterialGenerator())
            .Handle(new StageSigningKeyCommand(null, _keyring.ApproveSigning(KeyringAction.Stage), _keyring.Admin), CancellationToken.None);
        staged.IsError.Should().BeFalse(Codes(staged));
        return staged.Value.NextKid!;
    }

    private async Task<string> StagePublishAndWaitAsync()
    {
        var staged = await StageAsync();
        await _keyring.RestartAsync();
        _keyring.Clock.Advance(TimeSpan.FromHours(24));
        return staged;
    }

    private Task<ErrorOr.ErrorOr<SigningKeyringDto>> StageImport(string privateKeyPem) =>
        new StageSigningKeyCommandHandler(_keyring.SigningService, new SharedKeyMaterialGenerator()).Handle(
            new StageSigningKeyCommand(privateKeyPem, _keyring.ApproveSigning(KeyringAction.StageImport, value: privateKeyPem), _keyring.Admin),
            CancellationToken.None);

    private Task<ErrorOr.ErrorOr<SigningKeyringDto>> Promote(Guid approval, bool emergency = false) =>
        new PromoteSigningKeyCommandHandler(_keyring.SigningService).Handle(
            new PromoteSigningKeyCommand(emergency, approval, _keyring.Admin), CancellationToken.None);

    private Task<ErrorOr.ErrorOr<SigningKeyringDto>> Discard() =>
        new DiscardStagedSigningKeyCommandHandler(_keyring.SigningService).Handle(
            new DiscardStagedSigningKeyCommand(_keyring.ApproveSigning(KeyringAction.DiscardNext), _keyring.Admin), CancellationToken.None);

    private Task<ErrorOr.ErrorOr<SigningKeyringDto>> Retire(string kid) =>
        new RetireSigningKeyCommandHandler(_keyring.SigningService).Handle(
            new RetireSigningKeyCommand(kid, _keyring.ApproveSigning(KeyringAction.Retire, kid: kid), _keyring.Admin), CancellationToken.None);

    private Task<SigningKeyringDto> View() => _keyring.SigningService.ViewAsync(CancellationToken.None);

    private void NotSpent(Guid approval) =>
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "a refusal never spends the administrator's code");

    private static JwtSettings Settings => new() { Issuer = "https://auth.example.com", Audience = "auth-platform" };

    // A platform token signed now by the key this process signs with.
    private string SignedNow()
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("jti", Guid.NewGuid().ToString())]),
            Issuer = Settings.Issuer,
            Audience = Settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = _keyring.SigningKeys.Current
        }));
    }

    // Whether the platform scheme of this process accepts the token now.
    private bool Accepted(string token)
    {
        try
        {
            new JwtSecurityTokenHandler().ValidateToken(token, AccessTokenValidation.Platform(Settings, _keyring.SigningKeys), out _);
            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
    }

    // Whether the secrets file (or its backup) at path holds privateKeyPem anywhere it keeps a private key.
    private bool HoldsPrivateKey(string path, string privateKeyPem)
    {
        var json = Encoding.UTF8.GetString(_keyring.Protection.CreateProtector("AuthSystem.SecretManagement.v1").Unprotect(File.ReadAllBytes(path)));
        var secrets = JsonSerializer.Deserialize<SecretConfiguration>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return secrets.JwtPrivateKeyPem == privateKeyPem
            || secrets.SigningKeys.Values.Any(entry => entry.PrivateKeyPem == privateKeyPem);
    }

    // A PEM's base64 body, as JSON carries it (line breaks escaped).
    private static string Body(string pem) => pem.Split('\n')[2].Trim();

    private static string Codes<T>(ErrorOr.ErrorOr<T> result) => string.Join("; ", result.ErrorsOrEmptyList.Select(e => e.Code));
}
