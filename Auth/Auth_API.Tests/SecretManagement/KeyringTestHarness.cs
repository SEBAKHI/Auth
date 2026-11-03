using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Secrets;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// A real secrets file (encrypted with an ephemeral Data Protection key in a temp
/// folder), the process ring loaded from it, and the keyring change service over them,
/// with the database edges mocked: what the keyring tests drive end to end. The signing
/// keyring (S12) is loaded from the same file, as the "key material validation" step loads it,
/// with its own change service; <see cref="Restart"/> loads it again, as an API recycle does.
/// </summary>
/// <remarks>
/// The file is real on purpose: the properties under test (the ring survives a save,
/// the holder is swapped only after the save, the cutover is in the save that promotes)
/// are properties of the file and the lock, which a mocked secret service cannot show.
/// </remarks>
internal sealed class KeyringTestHarness : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "authsystem-keyring-harness", Guid.NewGuid().ToString("N"));

    public KeyringTestHarness(bool plainText = false, IDataProtectionProvider? protection = null)
    {
        Directory.CreateDirectory(_directory);
        Protection = protection ?? new EphemeralDataProtectionProvider();
        Secrets = new DpapiSecretService(
            Protection,
            Options.Create(new SecretManagementSettings { SecretFilePath = FilePath, AutoGenerateKeys = true }),
            NullLogger<DpapiSecretService>.Instance);
        Secrets.GenerateMissingKeysAsync(CancellationToken.None).GetAwaiter().GetResult();

        Holder = new HmacKeyringHolder(ReadFileRing());

        Impact
            .Setup(r => r.GetImpactAsync(It.IsAny<TimeSpan>(), It.IsAny<DateTime?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Snapshot);
        WebhookKeys
            .Setup(r => r.CountLiveByHmacKeyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WebhookCounts);
        WebhookKeys
            .Setup(r => r.GetHighestHmacKeyIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => HighestWebhookKeyId);

        StorageMode = new ResolvedSecretStorageMode(plainText ? "PlainText" : "Certificate", plainText);
        Service = new HmacKeyringChangeService(
            Secrets,
            Holder,
            Challenges.Service,
            Impact.Object,
            WebhookKeys.Object,
            Publisher.Object,
            StorageMode,
            Clock,
            new Mock<ILogger<HmacKeyringChangeService>>().Object);

        Challenges.WithConfirmedAdmin(Admin);

        Source = new SigningKeySource(SigningKeyLoader.PemKey, plainText, RsaKeyring.MigratedKidFrom(Jwt.KeyId));
        SigningKeys = new SigningKeyRingHolder(LoadSigningKeys());
        SigningService = NewSigningService();
    }

    public Guid Admin { get; } = Guid.NewGuid();

    public string FilePath => Path.Combine(_directory, "secrets.dpapi");

    public IDataProtectionProvider Protection { get; }

    public DpapiSecretService Secrets { get; }

    public HmacKeyringHolder Holder { get; }

    public SecretChallengeTestContext Challenges { get; } = new();

    public Mock<ISecretRotationImpactRepository> Impact { get; } = new();

    public Mock<IWebhookKeyRepository> WebhookKeys { get; } = new();

    public Mock<IPublisher> Publisher { get; } = new();

    public AdjustableTimeProvider Clock { get; } = new();

    public ResolvedSecretStorageMode StorageMode { get; }

    public HmacKeyringChangeService Service { get; }

    /// <summary>Where the process took its signing key from: the secrets file, or PlainText.</summary>
    public SigningKeySource Source { get; }

    /// <summary>The signing keys this process loaded at its last (simulated) start.</summary>
    public SigningKeyRingHolder SigningKeys { get; private set; }

    /// <summary>The signing keyring's change service over the process's keys.</summary>
    public RsaKeyringChangeService SigningService { get; private set; }

    /// <summary>The access-token lifetime a promote sizes the previous key's window with.</summary>
    public JwtSettings Jwt { get; } = new() { KeyId = "auth-key-1", AccessTokenLifetimeMinutes = 15 };

    /// <summary>The step-up bindings of both rings, as the challenge handlers look them up.</summary>
    public KeyringApprovals Approvals => new([new HmacKeyringApproval(Service, Clock), new RsaKeyringApproval(SigningService, Clock)]);

    /// <summary>What the impact repository answers; the latest expiries size a promote's window.</summary>
    public SecretRotationImpactSnapshot Snapshot { get; set; } = new()
    {
        UsersWithLiveAccessTokens = 0,
        UsersWithActiveSessions = 0,
        UsersWithActiveRefreshTokens = 0,
        UsersWithActiveIdpSessions = 0,
        PendingPasswordResets = 0,
        PendingTwoFactorChallenges = 0,
        ActiveWebhookKeys = 0
    };

    /// <summary>What the webhook repository answers for live keys by HMAC key.</summary>
    public IReadOnlyDictionary<int, int> WebhookCounts { get; set; } = new Dictionary<int, int>();

    /// <summary>The highest HMAC key id a stored webhook fingerprint names, revoked keys included (OI-217).</summary>
    public int HighestWebhookKeyId { get; set; }

    /// <summary>The ring the file holds now.</summary>
    public HmacKeyring ReadFileRing() =>
        HmacKeyringFile.Read(Secrets.LoadSecretsAsync(CancellationToken.None).GetAwaiter().GetResult()).Value;

    /// <summary>
    /// A verified approval for <paramref name="action"/>, bound as the request handler binds
    /// it: to the action and the target the file's ring names now.
    /// </summary>
    public Guid Approve(KeyringAction action, int? keyId = null, string? value = null)
    {
        var challengeId = Guid.NewGuid();
        var digest = KeyringStepUp.Digest(action, KeyringStepUp.TargetOf(action, ReadFileRing(), keyId), value);
        Challenges.WithApproval(challengeId, KeyringStepUp.OperationOf(action), Admin, digest);
        return challengeId;
    }

    /// <summary>A change request of <paramref name="action"/> spending <paramref name="challengeId"/>.</summary>
    public KeyringChangeRequest Request(
        KeyringAction action, Guid challengeId, int? keyId = null, string? value = null, string? material = null) =>
        new(action, KeyringStepUp.OperationOf(action), challengeId, Admin, value, material, keyId);

    /// <summary>Stages a generated key through the service, approval included.</summary>
    public async Task<HmacKeyring> StageAsync()
    {
        var material = KeyMaterial();
        var staged = await Service.ChangeAsync(
            Request(KeyringAction.Stage, Approve(KeyringAction.Stage), material: material),
            (ring, change) => ring.Stage(change.Material!, change.NowUtc),
            precheck: null,
            CancellationToken.None);
        staged.IsError.Should().BeFalse(string.Join("; ", staged.ErrorsOrEmptyList.Select(e => e.Code)));
        return Holder.Current;
    }

    /// <summary>Promotes the staged key through the service, approval included.</summary>
    public async Task<HmacKeyring> PromoteAsync()
    {
        var promoted = await Service.ChangeAsync(
            Request(KeyringAction.Promote, Approve(KeyringAction.Promote)),
            (ring, change) => ring.Promote(change.NowUtc, change.DemotedKeyVerifyUntilUtc),
            precheck: null,
            CancellationToken.None);
        promoted.IsError.Should().BeFalse(string.Join("; ", promoted.ErrorsOrEmptyList.Select(e => e.Code)));
        return Holder.Current;
    }

    /// <summary>The signing ring the file holds now.</summary>
    public RsaKeyring ReadSigningRing() =>
        RsaKeyringFile.Read(Secrets.LoadSecretsAsync(CancellationToken.None).GetAwaiter().GetResult(), Source.MigratedKid).Value;

    /// <summary>
    /// Takes the signing ring out of the file, leaving its single key: the file of a deployment
    /// that never rotated, whose key the next start migrates under <c>Jwt:KeyId</c>.
    /// </summary>
    public void RemoveSigningRing() =>
        Secrets.UpdateAsync<ErrorOr.Success>(
                secrets =>
                {
                    secrets.SigningKeys = new Dictionary<string, SigningKeyEntry>();
                    secrets.SigningCurrentKid = null;
                    secrets.SigningNextKid = null;
                    return ErrorOr.Result.Success;
                },
                overwriteBackup: false,
                CancellationToken.None)
            .GetAwaiter().GetResult();

    /// <summary>
    /// What an API recycle does to the signing keys: they are loaded again from the file, and the
    /// first start that loads a staged key records when it was published.
    /// </summary>
    public async Task<SigningKeyPublication.Outcome> RestartAsync()
    {
        var (ring, outcome, _) = await SigningKeyPublication.RecordAsync(Secrets, LoadSigningKeys(), Clock.UtcNow, CancellationToken.None);
        SigningKeys = new SigningKeyRingHolder(ring);
        SigningService = NewSigningService();
        return outcome;
    }

    /// <summary>The same, without awaiting: a test that only needs the keys reloaded.</summary>
    public void Restart() => RestartAsync().GetAwaiter().GetResult();

    /// <summary>
    /// A verified approval for a signing keyring <paramref name="action"/>, bound as the request
    /// handler binds it: to the action and the target the file's ring names now.
    /// </summary>
    public Guid ApproveSigning(KeyringAction action, string? kid = null, string? value = null)
    {
        var challengeId = Guid.NewGuid();
        var digest = KeyringStepUp.SigningDigest(action, KeyringStepUp.SigningTargetOf(action, ReadSigningRing(), kid, value), value);
        Challenges.WithApproval(challengeId, KeyringStepUp.SigningOperationOf(action), Admin, digest);
        return challengeId;
    }

    /// <summary>A signing keyring change request of <paramref name="action"/> spending <paramref name="challengeId"/>.</summary>
    public SigningKeyChangeRequest SigningRequest(KeyringAction action, Guid challengeId, string? kid = null, string? value = null) =>
        new(action, KeyringStepUp.SigningOperationOf(action), challengeId, Admin, value, kid);

    /// <summary>A new signing key, as the server generates it.</summary>
    public static SigningKey NewSigningKey() =>
        RsaKeyringFile.FromPrivateKeyPem(Auth.Shared.Configuration.KeyMaterialGenerator.GenerateRsaKeyPair().PrivateKeyPem).Value;

    private SigningKeyRing LoadSigningKeys()
    {
        var secrets = Secrets.LoadSecretsAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (!Source.HoldsRing)
        {
            var rsa = System.Security.Cryptography.RSA.Create();
            rsa.ImportFromPem(secrets.JwtPrivateKeyPem);
            return SigningKeyRing.Single(rsa, Source.MigratedKid, Source);
        }

        return SigningKeyRing.Load(RsaKeyringFile.Read(secrets, Source.MigratedKid).Value, Source);
    }

    private RsaKeyringChangeService NewSigningService() => new(
        Secrets,
        SigningKeys,
        Challenges.Service,
        Publisher.Object,
        new TestHelpers.TestOptions<JwtSettings>(Jwt),
        Clock,
        new Mock<ILogger<RsaKeyringChangeService>>().Object);

    /// <summary>New key material, as the server generates it.</summary>
    public static string KeyMaterial() => Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }
}
