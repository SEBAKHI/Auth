using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Features.Secrets.ImportHmacKey;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// Unit tests for ImportHmacKeyCommandHandler (bring-your-own-keys HMAC import): since the
/// keyring (S11), an alias of an emergency promote of the supplied key, whose confirmation
/// keeps today's digest of the material.
/// </summary>
public sealed class ImportHmacKeyCommandHandlerTests : IDisposable
{
    private readonly KeyringTestHarness _keyring = new();

    public void Dispose() => _keyring.Dispose();

    private ImportHmacKeyCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _keyring.Service,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }));

    private static string CreateHmacKeyBase64(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>Arranges a live approval bound to this exact key material, as today's console raises it.</summary>
    private Guid ApprovalFor(string keyBase64)
    {
        var challengeId = Guid.NewGuid();
        _keyring.Challenges.WithApproval(
            challengeId,
            SecretOperation.ImportHmacKey,
            _keyring.Admin,
            SecretPayloadDigest.Compute(keyBase64));
        return challengeId;
    }

    [Fact]
    public async Task Handle_ValidKey_MakesItTheCurrentKey_AsAnEmergency()
    {
        var keyBase64 = CreateHmacKeyBase64();

        var result = await CreateHandler().Handle(
            new ImportHmacKeyCommand(keyBase64, ApprovalFor(keyBase64), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var ring = _keyring.ReadFileRing();
        ring.Current.Material.ToArray().Should().Equal(Convert.FromBase64String(keyBase64));
        ring.Find(1)!.Compromised.Should().BeTrue("the import alias keeps today's meaning: the old key stops verifying");
        _keyring.Holder.Current.Current.Id.Should().Be(ring.Current.Id, "the process issues under the imported key at once");
        _keyring.Publisher.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e =>
                    e.Operation == SecretOperation.ImportHmacKey && e.Action == KeyringAction.PromoteEmergency),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflict()
    {
        var keyBase64 = CreateHmacKeyBase64();

        var result = await CreateHandler(storageMode: "PlainText").Handle(
            new ImportHmacKeyCommand(keyBase64, ApprovalFor(keyBase64), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ImportNotSupportedInPlainText");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NotBase64_ReturnsInvalidKeyMaterial()
    {
        var result = await CreateHandler().Handle(
            new ImportHmacKeyCommand("not base64!!", ApprovalFor("not base64!!"), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task Handle_KeyBelowMinimumLength_ReturnsInvalidKeyMaterial()
    {
        // 16 bytes is below the 32-byte (256-bit) minimum.
        var keyBase64 = CreateHmacKeyBase64(bytes: 16);

        var result = await CreateHandler().Handle(
            new ImportHmacKeyCommand(keyBase64, ApprovalFor(keyBase64), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
    }

    [Fact]
    public async Task Handle_WithoutApproval_DoesNotImport()
    {
        var result = await CreateHandler().Handle(
            new ImportHmacKeyCommand(CreateHmacKeyBase64(), Guid.NewGuid(), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task Handle_KeyMaterialSwappedAfterConfirmation_DoesNotImport()
    {
        // The administrator confirmed one key and submitted another.
        var result = await CreateHandler().Handle(
            new ImportHmacKeyCommand(CreateHmacKeyBase64(), ApprovalFor(CreateHmacKeyBase64()), _keyring.Admin),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task Handle_TheCurrentKeyAgain_IsRefusedBeforeTheApprovalIsSpent()
    {
        // Before the keyring, re-importing the stored value was a no-op. As an emergency it
        // would mark the very key it keeps compromised, so it is refused, and the code the
        // administrator was emailed stays usable.
        var current = Convert.ToBase64String(_keyring.ReadFileRing().Current.Material.ToArray());
        var approval = ApprovalFor(current);

        var result = await CreateHandler().Handle(
            new ImportHmacKeyCommand(current, approval, _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        result.FirstError.Description.Should().Contain("already HMAC key 1");
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "the confirmation is not spent on an import that cannot happen");
        var ring = _keyring.ReadFileRing();
        ring.Current.Id.Should().Be(1);
        ring.Find(1)!.Compromised.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AKeyringStageImportApproval_IsNotSpentOnTheAlias()
    {
        // The same operation, a different digest: an approval raised in the keyring panel
        // to stage this key must not replace the current key at once.
        var keyBase64 = CreateHmacKeyBase64();
        var keyringApproval = _keyring.Approve(KeyringAction.StageImport, value: keyBase64);

        var result = await CreateHandler().Handle(
            new ImportHmacKeyCommand(keyBase64, keyringApproval, _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
    }
}
