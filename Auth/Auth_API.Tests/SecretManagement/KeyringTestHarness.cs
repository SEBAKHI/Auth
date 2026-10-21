using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Secrets;
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
/// with the database edges mocked: what the keyring tests drive end to end.
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
