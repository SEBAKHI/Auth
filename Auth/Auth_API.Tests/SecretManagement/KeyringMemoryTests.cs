using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Features.Secrets.DiscardStagedHmacKey;
using Auth.Application.Features.Secrets.GenerateHmacKey;
using Auth.Application.Features.Secrets.ImportHmacKey;
using Auth.Application.Features.Secrets.PromoteHmacKey;
using Auth.Application.Features.Secrets.ReinstateHmacKey;
using Auth.Application.Features.Secrets.RetireHmacKey;
using Auth.Application.Features.Secrets.StageHmacKey;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth.Infrastructure.Security;
using Auth_API.Common.Errors;
using Auth_API.Tests.Helpers;
using ErrorOr;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// OI-217 parts 2 and 3, end to end through the command handlers and a real secrets file: the
/// keyring remembers every key that left it and refuses its material on every path that takes
/// new material, and a key id is never given to a second key, whatever the file or the webhook
/// table still names.
/// </summary>
public sealed class KeyringMemoryTests : IDisposable
{
    private static readonly JsonSerializerOptions FileJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly KeyringTestHarness _keyring = new();

    public void Dispose() => _keyring.Dispose();

    [Fact]
    public async Task AfterARetire_TheNextKey_TakesAnIdNoKeyHadBefore()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        (await Reinstate(2)).IsError.Should().BeFalse();
        (await Retire(3)).IsError.Should().BeFalse();

        var staged = await _keyring.StageAsync();

        staged.Next!.Id.Should().Be(4, "key 3 was used; an id never names two keys");
    }

    [Fact]
    public async Task AfterADiscard_TheNextKey_TakesAnIdNoKeyHadBefore()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        await _keyring.StageAsync();
        (await Discard()).IsError.Should().BeFalse();

        var staged = await _keyring.StageAsync();

        staged.Next!.Id.Should().Be(4);
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("stage-import")]
    [InlineData("emergency")]
    public async Task AFileThatRecordsNoHighestId_GivesTheNextKeyAnIdAboveEveryStoredWebhookKey(string path)
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        await ForgetTheMemory();
        // A webhook key stored under hk5$, revoked or not: the repository reads every row.
        _keyring.HighestWebhookKeyId = 5;

        var changed = await Offer(path, KeyringTestHarness.KeyMaterial());

        changed.IsError.Should().BeFalse(string.Join("; ", changed.ErrorsOrEmptyList.Select(e => e.Code)));
        var newKey = path == "emergency" ? _keyring.ReadFileRing().Current : _keyring.ReadFileRing().Next!;
        newKey.Id.Should().Be(6, "the ring holds keys 1 and 2, but a stored webhook fingerprint names key 5");
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("stage-import")]
    [InlineData("emergency")]
    [InlineData("generate alias")]
    [InlineData("import alias")]
    public async Task TheMaterialOfARetiredKey_IsRefusedOnEveryPath_NamingTheKeyNeverTheMaterial(string path)
    {
        // Key 1 leaks: the emergency marks it compromised, then it is retired.
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        var leaked = Convert.ToBase64String(_keyring.ReadFileRing().Find(1)!.Material);
        (await Emergency(KeyringTestHarness.KeyMaterial())).IsError.Should().BeFalse();
        (await Retire(1)).IsError.Should().BeFalse();
        var spentBefore = ApprovalsSpent();

        var result = await Offer(path, leaked);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        ErrorStatusMap.ToStatusCode(result.FirstError).Should().Be(400, "the administrator supplied it");
        result.FirstError.Description.Should().Contain("HMAC key 1").And.Contain("retired after it was marked compromised");
        result.FirstError.Description.Should().NotContain(leaked);
        string.Join(" ", (object[])result.FirstError.Metadata!["args"]).Should().NotContain(leaked);
        ApprovalsSpent().Should().Be(spentBefore, "the material is refused before the confirmation is spent");
        _keyring.ReadFileRing().Keys.Select(key => Convert.ToBase64String(key.Material)).Should().NotContain(leaked);
    }

    [Fact]
    public async Task TheMaterialOfADiscardedKey_IsRefused_AsDiscarded()
    {
        await _keyring.StageAsync();
        var discarded = Convert.ToBase64String(_keyring.ReadFileRing().Next!.Material);
        (await Discard()).IsError.Should().BeFalse();

        var result = await Offer("stage-import", discarded);

        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        result.FirstError.Description.Should().Contain("HMAC key 2").And.Contain("discarded").And.NotContain("compromised");
    }

    [Fact]
    public async Task TheMemoryAndTheHighestId_SurviveASaveAndAReload_ThroughBothModels()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        // A change that adds no key still records the id a stored webhook key names.
        _keyring.HighestWebhookKeyId = 9;
        (await Retire(1)).IsError.Should().BeFalse();

        var saved = _keyring.ReadFileRing();
        saved.Removed.Should().ContainSingle(key => key.Id == 1 && key.Reason == HmacKeyRemoval.Retired);
        saved.HighestKeyId.Should().Be(9);

        // Through the configuration provider's model and back, as text only: a member that one
        // model lacks is dropped here, whatever the parity test says about the shapes.
        var json = Decrypt();
        json.Should().Contain("\"hmacRemovedKeys\"").And.Contain("\"hmacHighestKeyId\": 9");
        var shared = JsonSerializer.Deserialize<Auth.Shared.Configuration.SecretConfiguration>(json, FileJson)!;
        var back = JsonSerializer.Deserialize<SecretConfiguration>(JsonSerializer.Serialize(shared, FileJson), FileJson)!;

        var reloaded = HmacKeyringFile.Read(back).Value;
        reloaded.Removed.Should().BeEquivalentTo(saved.Removed);
        reloaded.HighestKeyId.Should().Be(9);
    }

    [Fact]
    public async Task AFileFromABuildWithoutTheMemory_Loads_AndItsFirstSaveWritesBothMembers()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        // The file as a build without the memory saves it: neither member at all.
        var older = JsonNode.Parse(Decrypt())!.AsObject();
        older.Remove("hmacRemovedKeys").Should().BeTrue();
        older.Remove("hmacHighestKeyId").Should().BeTrue();
        Encrypt(older.ToJsonString());

        _keyring.ReadFileRing().HighestKeyId.Should().Be(2, "an older file loads, and the ids it holds count");

        (await Retire(1)).IsError.Should().BeFalse();

        var saved = JsonNode.Parse(Decrypt())!.AsObject();
        saved["hmacHighestKeyId"]!.GetValue<int>().Should().Be(2);
        saved["hmacRemovedKeys"]!.AsArray().Should().ContainSingle()
            .Which!["id"]!.GetValue<int>().Should().Be(1);
    }

    private Task<ErrorOr<HmacKeyringDto>> Offer(string path, string material) => path switch
    {
        "stage" => new StageHmacKeyCommandHandler(_keyring.Service, new FixedKeyMaterial(material)).Handle(
            new StageHmacKeyCommand(null, _keyring.Approve(KeyringAction.Stage), _keyring.Admin), CancellationToken.None),
        "stage-import" => new StageHmacKeyCommandHandler(_keyring.Service, new SharedKeyMaterialGenerator()).Handle(
            new StageHmacKeyCommand(material, _keyring.Approve(KeyringAction.StageImport, value: material), _keyring.Admin),
            CancellationToken.None),
        "emergency" => Emergency(material),
        "generate alias" => GenerateAlias(material),
        "import alias" => ImportAlias(material),
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, null)
    };

    private Task<ErrorOr<HmacKeyringDto>> Emergency(string material) =>
        new PromoteHmacKeyCommandHandler(_keyring.Service, new FixedKeyMaterial(material)).Handle(
            new PromoteHmacKeyCommand(Emergency: true, _keyring.Approve(KeyringAction.PromoteEmergency), _keyring.Admin),
            CancellationToken.None);

    // generate/hmac and import/hmac, confirmed as a console from before the keyring confirms them.
    private async Task<ErrorOr<HmacKeyringDto>> GenerateAlias(string material)
    {
        var approval = Guid.NewGuid();
        _keyring.Challenges.WithApproval(approval, SecretOperation.GenerateHmacKey, _keyring.Admin);
        var result = await new GenerateHmacKeyCommandHandler(_keyring.Service, new FixedKeyMaterial(material)).Handle(
            new GenerateHmacKeyCommand(approval, _keyring.Admin), CancellationToken.None);
        if (result.IsError)
        {
            return result.Errors;
        }

        return await _keyring.Service.ViewAsync(CancellationToken.None);
    }

    private async Task<ErrorOr<HmacKeyringDto>> ImportAlias(string material)
    {
        var approval = Guid.NewGuid();
        _keyring.Challenges.WithApproval(approval, SecretOperation.ImportHmacKey, _keyring.Admin, SecretPayloadDigest.Compute(material));
        var result = await new ImportHmacKeyCommandHandler(
                _keyring.Service, TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = "Certificate" }))
            .Handle(new ImportHmacKeyCommand(material, approval, _keyring.Admin), CancellationToken.None);
        if (result.IsError)
        {
            return result.Errors;
        }

        return await _keyring.Service.ViewAsync(CancellationToken.None);
    }

    private Task<ErrorOr<HmacKeyringDto>> Reinstate(int keyId) =>
        new ReinstateHmacKeyCommandHandler(_keyring.Service).Handle(
            new ReinstateHmacKeyCommand(keyId, _keyring.Approve(KeyringAction.Reinstate, keyId: keyId), _keyring.Admin),
            CancellationToken.None);

    private Task<ErrorOr<HmacKeyringDto>> Retire(int keyId) =>
        new RetireHmacKeyCommandHandler(_keyring.Service, _keyring.WebhookKeys.Object).Handle(
            new RetireHmacKeyCommand(keyId, _keyring.Approve(KeyringAction.Retire, keyId: keyId), _keyring.Admin),
            CancellationToken.None);

    private Task<ErrorOr<HmacKeyringDto>> Discard() =>
        new DiscardStagedHmacKeyCommandHandler(_keyring.Service).Handle(
            new DiscardStagedHmacKeyCommand(_keyring.Approve(KeyringAction.DiscardNext), _keyring.Admin),
            CancellationToken.None);

    // The file as a build without the memory left it: no removed keys, no highest id.
    private Task ForgetTheMemory() =>
        _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                secrets.HmacRemovedKeys.Clear();
                secrets.HmacHighestKeyId = 0;
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

    private int ApprovalsSpent() =>
        _keyring.Challenges.ChallengeRepository.Invocations.Count(call => call.Method.Name == "TryConsumeAsync");

    private string Decrypt() =>
        Encoding.UTF8.GetString(_keyring.Protection.CreateProtector("AuthSystem.SecretManagement.v1")
            .Unprotect(File.ReadAllBytes(_keyring.FilePath)));

    private void Encrypt(string json) =>
        File.WriteAllBytes(
            _keyring.FilePath,
            _keyring.Protection.CreateProtector("AuthSystem.SecretManagement.v1").Protect(Encoding.UTF8.GetBytes(json)));

    private sealed class FixedKeyMaterial(string material) : IKeyMaterialGenerator
    {
        public string GenerateHmacKeyBase64() => material;
    }
}
