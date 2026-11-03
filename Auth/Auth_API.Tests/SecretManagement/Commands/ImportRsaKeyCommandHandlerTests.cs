using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Features.Secrets.ImportRsaKey;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.SecretManagement.Commands;

/// <summary>
/// ImportRsaKeyCommandHandler (bring-your-own-keys RSA import). Since the signing keyring (S12)
/// it is an alias of a staged import of the key and its emergency promote, over the real secrets
/// file; its confirmation is still bound to a digest of the exact key material.
/// </summary>
public sealed class ImportRsaKeyCommandHandlerTests : IDisposable
{
    private readonly KeyringTestHarness _keyring = new();

    public void Dispose() => _keyring.Dispose();

    private ImportRsaKeyCommandHandler CreateHandler(string storageMode = "Dpapi") =>
        new(
            _keyring.SigningService,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = storageMode }));

    private static string CreateRsaPrivateKeyPem(int keySizeBits = 2048)
    {
        using var rsa = RSA.Create(keySizeBits);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>Arranges a live approval bound to this exact key material, as before the keyring.</summary>
    private Guid ApprovalFor(string privateKeyPem)
    {
        var challengeId = Guid.NewGuid();
        _keyring.Challenges.WithApproval(
            challengeId,
            SecretOperation.ImportRsaKey,
            _keyring.Admin,
            SecretPayloadDigest.Compute(privateKeyPem));
        return challengeId;
    }

    [Fact]
    public async Task Handle_ValidKey_ImportsAndReturnsDerivedPublicKey()
    {
        var before = _keyring.ReadSigningRing();
        var privateKeyPem = CreateRsaPrivateKeyPem();

        var result = await CreateHandler().Handle(
            new ImportRsaKeyCommand(privateKeyPem, ApprovalFor(privateKeyPem), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse(string.Join("; ", result.ErrorsOrEmptyList.Select(e => e.Code)));
        result.Value.Should().Contain("BEGIN PUBLIC KEY");
        var after = _keyring.ReadSigningRing();
        after.Current.Kid.Should().Be(SigningKeyThumbprint.OfPem(privateKeyPem), "the imported key is current, named by its thumbprint");
        after.Find(before.Current.Kid)!.Compromised.Should().BeTrue("the replaced key is closed, as an emergency closes it");
        _keyring.Publisher.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e => e.Operation == SecretOperation.ImportRsaKey && e.Action == KeyringAction.PromoteEmergency),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlainTextMode_ReturnsConflictAndDoesNotImport()
    {
        var before = _keyring.ReadSigningRing();
        var privateKeyPem = CreateRsaPrivateKeyPem();

        var result = await CreateHandler(storageMode: "PlainText").Handle(
            new ImportRsaKeyCommand(privateKeyPem, ApprovalFor(privateKeyPem), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ImportNotSupportedInPlainText");
        _keyring.ReadSigningRing().Current.Kid.Should().Be(before.Current.Kid);
    }

    [Fact]
    public async Task Handle_NotAPem_ReturnsInvalidKeyMaterial()
    {
        var result = await CreateHandler().Handle(
            new ImportRsaKeyCommand("not-a-pem", ApprovalFor("not-a-pem"), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
    }

    [Fact]
    public async Task Handle_KeyBelowMinimumSize_ReturnsInvalidKeyMaterial()
    {
        var privateKeyPem = CreateRsaPrivateKeyPem(keySizeBits: 1024);

        var result = await CreateHandler().Handle(
            new ImportRsaKeyCommand(privateKeyPem, ApprovalFor(privateKeyPem), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
    }

    [Fact]
    public async Task Handle_PublicKeyOnly_ReturnsInvalidKeyMaterial()
    {
        // Supplying a public key PEM (no private component) must be rejected.
        using var rsa = RSA.Create(2048);
        var publicKeyOnlyPem = rsa.ExportSubjectPublicKeyInfoPem();

        var result = await CreateHandler().Handle(
            new ImportRsaKeyCommand(publicKeyOnlyPem, ApprovalFor(publicKeyOnlyPem), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
    }

    [Fact]
    public async Task Handle_WithoutApproval_DoesNotImport()
    {
        var before = _keyring.ReadSigningRing();

        var result = await CreateHandler().Handle(
            new ImportRsaKeyCommand(CreateRsaPrivateKeyPem(), Guid.NewGuid(), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadSigningRing().Current.Kid.Should().Be(before.Current.Kid);
    }

    [Fact]
    public async Task Handle_KeyMaterialSwappedAfterConfirmation_DoesNotImport()
    {
        // The administrator confirmed one key and submitted another.
        var before = _keyring.ReadSigningRing();
        var confirmedKey = CreateRsaPrivateKeyPem();
        var swappedKey = CreateRsaPrivateKeyPem();

        var result = await CreateHandler().Handle(
            new ImportRsaKeyCommand(swappedKey, ApprovalFor(confirmedKey), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadSigningRing().Current.Kid.Should().Be(before.Current.Kid);
    }

    [Fact]
    public async Task Handle_TheKeyItReplaced_IsNeverImportedAgain()
    {
        // The key a leak closed is remembered: brought back, it would sign again with nothing
        // to say it leaked (S12, T6). Refused before the approval is spent.
        var leaked = CreateRsaPrivateKeyPem();
        (await CreateHandler().Handle(new ImportRsaKeyCommand(leaked, ApprovalFor(leaked), _keyring.Admin), CancellationToken.None))
            .IsError.Should().BeFalse();
        var replacement = CreateRsaPrivateKeyPem();
        (await CreateHandler().Handle(new ImportRsaKeyCommand(replacement, ApprovalFor(replacement), _keyring.Admin), CancellationToken.None))
            .IsError.Should().BeFalse();
        var approval = ApprovalFor(leaked);

        var again = await CreateHandler().Handle(new ImportRsaKeyCommand(leaked, approval, _keyring.Admin), CancellationToken.None);

        again.FirstError.Code.Should().Be("Secret.KeyringKeyReferenced");
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "a refusal never spends the administrator's code");
    }
}
