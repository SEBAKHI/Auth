using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Infrastructure.Security;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S11 (D-S11B-1, ARR-0384): there is no switch that turns purpose subkeys on; the first
/// promote of a key other than 1 is the cutover, and it records the cutover in the very
/// save that makes key 2 current. A file that ever held key 2 current without a cutover
/// would refuse the next boot (HmacKeyring.Create), so a cutover written by a second save
/// would leave a window in which a restart cannot start.
/// </summary>
public sealed class PromoteRecordsTheCutoverTests : IDisposable
{
    private readonly KeyringTestHarness _keyring = new();

    public void Dispose() => _keyring.Dispose();

    [Fact]
    public async Task TheFirstPromoteOfKey2_RecordsTheCutover_InTheSameSave()
    {
        await _keyring.StageAsync();
        var recording = new RecordingSecretService(_keyring.Secrets);
        var service = new HmacKeyringChangeService(
            recording,
            _keyring.Holder,
            _keyring.Challenges.Service,
            _keyring.Impact.Object,
            _keyring.WebhookKeys.Object,
            _keyring.Publisher.Object,
            _keyring.StorageMode,
            _keyring.Clock,
            new Mock<ILogger<HmacKeyringChangeService>>().Object);

        var result = await service.ChangeAsync(
            _keyring.Request(KeyringAction.Promote, _keyring.Approve(KeyringAction.Promote)),
            (ring, change) => ring.Promote(change.NowUtc, change.DemotedKeyVerifyUntilUtc),
            precheck: null,
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        recording.Saves.Should().ContainSingle("one promote is one save");
        var save = recording.Saves[0];
        save.CurrentKeyId.Should().Be(2);
        save.LegacyCutoverUtc.Should().NotBeNull("the save that makes key 2 current carries the cutover");
        save.LegacyVerifyUntilUtc.Should().NotBeNull();
        save.Key1DemotedAtUtc.Should().Be(save.LegacyCutoverUtc, "key 1 stopped issuing at the cutover");

        _keyring.Holder.Current.LegacyCutoverUtc.Should().Be(save.LegacyCutoverUtc);
        _keyring.Holder.Current.Issuing.IsLegacy.Should().BeFalse("the process issues under key 2's subkeys at once");
    }

    [Fact]
    public async Task StagingKey2_RecordsNoCutover()
    {
        await _keyring.StageAsync();

        var ring = _keyring.ReadFileRing();

        ring.LegacyCutoverUtc.Should().BeNull("only the promote that stops key 1 issuing is the cutover");
        ring.Issuing.IsLegacy.Should().BeTrue();
    }

    private sealed record SavedRing(int CurrentKeyId, DateTime? LegacyCutoverUtc, DateTime? LegacyVerifyUntilUtc, DateTime? Key1DemotedAtUtc);

    /// <summary>Records what every save of the file held, around the real service.</summary>
    private sealed class RecordingSecretService(DpapiSecretService inner) : IDpapiSecretService
    {
        public List<SavedRing> Saves { get; } = [];

        public bool SecretFileExists() => inner.SecretFileExists();
        public string GetSecretFilePath() => inner.GetSecretFilePath();
        public Task<SecretConfiguration> LoadSecretsAsync(CancellationToken cancellationToken) => inner.LoadSecretsAsync(cancellationToken);

        public Task<ErrorOr<T>> UpdateAsync<T>(
            Func<SecretConfiguration, ErrorOr<T>> change, bool overwriteBackup, CancellationToken cancellationToken, Action<T>? onSaved = null) =>
            inner.UpdateAsync<T>(
                secrets =>
                {
                    var result = change(secrets);
                    if (!result.IsError)
                    {
                        Saves.Add(new SavedRing(
                            secrets.HmacCurrentKeyId,
                            secrets.HmacLegacyCutoverUtc,
                            secrets.HmacLegacyVerifyUntilUtc,
                            secrets.HmacKeys.GetValueOrDefault(HmacKeyring.LegacyKeyId)?.DemotedAtUtc));
                    }

                    return result;
                },
                overwriteBackup,
                cancellationToken,
                onSaved);

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) => inner.GetSecretAsync(key, cancellationToken);
        public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken) => inner.SetSecretAsync(key, value, cancellationToken);
        public Task<bool> RemoveSecretAsync(string key, CancellationToken cancellationToken) => inner.RemoveSecretAsync(key, cancellationToken);
        public Task<string> GenerateRsaKeyPairAsync(CancellationToken cancellationToken) => inner.GenerateRsaKeyPairAsync(cancellationToken);
        public Task<string> GenerateGatewayTokenAsync(CancellationToken cancellationToken) => inner.GenerateGatewayTokenAsync(cancellationToken);
        public Task<KeyGenerationResult> GenerateMissingKeysAsync(CancellationToken cancellationToken) => inner.GenerateMissingKeysAsync(cancellationToken);
        public Task ImportRsaKeyPairAsync(string privateKeyPem, string publicKeyPem, CancellationToken cancellationToken) => inner.ImportRsaKeyPairAsync(privateKeyPem, publicKeyPem, cancellationToken);
        public Task ImportGatewayTokenAsync(string token, CancellationToken cancellationToken) => inner.ImportGatewayTokenAsync(token, cancellationToken);
        public Task<SecretStatusResult> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);
    }
}
