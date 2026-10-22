using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Features.Secrets.DiscardStagedHmacKey;
using Auth.Application.Features.Secrets.GenerateHmacKey;
using Auth.Application.Features.Secrets.GetHmacKeyring;
using Auth.Application.Features.Secrets.ImportHmacKey;
using Auth.Application.Features.Secrets.PromoteHmacKey;
using Auth.Application.Features.Secrets.ReinstateHmacKey;
using Auth.Application.Features.Secrets.RequestSecretOperationChallenge;
using Auth.Application.Features.Secrets.RetireHmacKey;
using Auth.Application.Features.Secrets.StageHmacKey;
using Auth.Application.Features.Secrets.VerifySecretOperationChallenge;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Application.Security.Keyring;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S11 TS5 (KR3): every keyring change is confirmed with the HMAC step-up, bound to its
/// action and to the key it applies to, both read by the server from the secrets file;
/// it is refused where the ring cannot be kept; it is swapped into the process only after
/// it was saved; and its cost is shown for the action it is.
/// </summary>
public sealed class KeyringCommandHandlerTests : IDisposable
{
    private readonly KeyringTestHarness _keyring = new();

    public void Dispose() => _keyring.Dispose();

    private PromoteHmacKeyCommandHandler Promote() => new(_keyring.Service, new SharedKeyMaterialGenerator());

    [Theory]
    [InlineData(KeyringAction.Stage, SecretOperation.GenerateHmacKey)]
    [InlineData(KeyringAction.StageImport, SecretOperation.ImportHmacKey)]
    [InlineData(KeyringAction.Promote, SecretOperation.GenerateHmacKey)]
    [InlineData(KeyringAction.PromoteEmergency, SecretOperation.GenerateHmacKey)]
    [InlineData(KeyringAction.DiscardNext, SecretOperation.GenerateHmacKey)]
    [InlineData(KeyringAction.Reinstate, SecretOperation.GenerateHmacKey)]
    [InlineData(KeyringAction.Retire, SecretOperation.GenerateHmacKey)]
    public void EveryKeyringAction_IsConfirmedByAnHmacOperation_SoTheOperationCheckNeedsNoNewValue(
        KeyringAction action, SecretOperation operation)
    {
        // E23 does not fire: the CHECK on SecretOperationChallenges.Operation keeps its six values.
        KeyringStepUp.OperationOf(action).Should().Be(operation);
        ((int)KeyringStepUp.OperationOf(action)).Should().BeOneOf(2, 5);
    }

    [Fact]
    public async Task APromoteApproval_IsNeverSpentOnAnEmergency()
    {
        await _keyring.StageAsync();
        var promoteApproval = _keyring.Approve(KeyringAction.Promote);

        var result = await Promote().Handle(new PromoteHmacKeyCommand(Emergency: true, promoteApproval, _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task AnEmergencyApproval_IsNeverSpentOnAScheduledPromote()
    {
        await _keyring.StageAsync();
        var emergencyApproval = _keyring.Approve(KeyringAction.PromoteEmergency);

        var result = await Promote().Handle(new PromoteHmacKeyCommand(Emergency: false, emergencyApproval, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
    }

    [Fact]
    public async Task TheTarget_IsReadFromTheFile_SoARingThatMovedRefusesTheApproval()
    {
        // Approved while the file staged key 2; by the time it is spent, key 2 was discarded
        // and another key staged in its place, under the same id: promoting "the staged key"
        // now would promote what was never approved.
        await _keyring.StageAsync();
        var approvedForKey2 = _keyring.Approve(KeyringAction.Promote);
        await new DiscardStagedHmacKeyCommandHandler(_keyring.Service).Handle(
            new DiscardStagedHmacKeyCommand(_keyring.Approve(KeyringAction.DiscardNext), _keyring.Admin), CancellationToken.None);
        _keyring.Clock.Advance(TimeSpan.FromMinutes(1));
        await _keyring.StageAsync();

        var result = await Promote().Handle(new PromoteHmacKeyCommand(false, approvedForKey2, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task AReinstateApproval_IsBoundToItsKey()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        var approvedForKey1 = _keyring.Approve(KeyringAction.Reinstate, keyId: 1);

        var result = await new ReinstateHmacKeyCommandHandler(_keyring.Service).Handle(
            new ReinstateHmacKeyCommand(2, approvedForKey1, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");
        _keyring.ReadFileRing().Current.Id.Should().Be(3);
    }

    [Fact]
    public async Task TheResolvedPlainTextMode_RefusesTheKeyring_WhateverTheConfiguredModeSays()
    {
        // Development without a certificate falls back to PlainText while the configured
        // mode still reads Certificate: a ring saved to a file that process never reads would
        // be lost at its next start.
        using var plain = new KeyringTestHarness(plainText: true);
        var request = new RequestSecretOperationChallengeCommandHandler(
            plain.Challenges.Service,
            plain.Service,
            TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = "Certificate" }),
            plain.Publisher.Object,
            plain.Clock,
            new Mock<ILogger<RequestSecretOperationChallengeCommandHandler>>().Object);

        var raised = await request.Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateHmacKey, null, plain.Admin, null, KeyringAction.Stage),
            CancellationToken.None);
        var staged = await new StageHmacKeyCommandHandler(plain.Service, new SharedKeyMaterialGenerator()).Handle(
            new StageHmacKeyCommand(null, Guid.NewGuid(), plain.Admin), CancellationToken.None);
        var read = await new GetHmacKeyringQueryHandler(plain.Service).Handle(new GetHmacKeyringQuery(), CancellationToken.None);

        raised.FirstError.Code.Should().Be("Secret.KeyringUnavailable");
        staged.FirstError.Code.Should().Be("Secret.KeyringUnavailable");
        read.FirstError.Code.Should().Be("Secret.KeyringUnavailable");
        plain.Challenges.NotificationService.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "no code is emailed for a change that cannot happen");
    }

    [Fact]
    public async Task TheConfirmationEmail_NamesTheOperationAndTheAction()
    {
        await _keyring.StageAsync();
        NotificationRequest? sent = null;
        _keyring.Challenges.NotificationService
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((NotificationRequest n, CancellationToken _) => sent = n)
            .ReturnsAsync(Result.Success);

        var raised = await RequestHandler().Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateHmacKey, null, _keyring.Admin, null, KeyringAction.Promote),
            CancellationToken.None);

        raised.IsError.Should().BeFalse();
        sent!.Variables["OperationCode"].Should().Be("GenerateHmacKey/Promote");
    }

    [Fact]
    public async Task AChallengeForAChangeThatCannotHappen_IsRefusedBeforeACodeIsEmailed()
    {
        var raised = await RequestHandler().Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateHmacKey, null, _keyring.Admin, null, KeyringAction.Promote),
            CancellationToken.None);

        raised.FirstError.Code.Should().Be("Secret.KeyringNoNextKey");
        _keyring.Challenges.NotificationService.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheChallengeIsBound_ToTheDigestOfTheActionAndTheFilesTarget()
    {
        await _keyring.StageAsync();
        SecretOperationChallenge? stored = null;
        _keyring.Challenges.ChallengeRepository
            .Setup(r => r.CreateAsync(It.IsAny<SecretOperationChallenge>(), It.IsAny<CancellationToken>()))
            .Callback((SecretOperationChallenge c, CancellationToken _) => stored = c);

        await RequestHandler().Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateHmacKey, null, _keyring.Admin, null, KeyringAction.Promote),
            CancellationToken.None);

        var staged = _keyring.ReadFileRing().Next!;
        stored!.PayloadHash.Should().Be(SecretPayloadDigest.Compute(
            $"keyring:hmac/Promote/2@{staged.CreatedAtUtc:O}\n"));
    }

    [Fact]
    public async Task Verify_WithAnotherAction_IsRefused_AndWithItsOwn_CostsThatAction()
    {
        await _keyring.StageAsync();
        var target = KeyringStepUp.TargetOf(KeyringAction.Promote, _keyring.ReadFileRing(), null);
        var challengeId = OpenChallenge(KeyringStepUp.Digest(KeyringAction.Promote, target, null));
        _keyring.Snapshot = _keyring.Snapshot with { UsersWithRefreshTokensBeyondOverlap = 3, UsersWithActiveRefreshTokens = 40 };

        var claimedEmergency = await VerifyHandler().Handle(
            new VerifySecretOperationChallengeCommand(challengeId, SecretChallengeTestContext.CorrectCode, _keyring.Admin, KeyringAction.PromoteEmergency),
            CancellationToken.None);
        claimedEmergency.FirstError.Code.Should().Be("Secret.ChallengeNotApproved");

        var otherId = OpenChallenge(KeyringStepUp.Digest(KeyringAction.Promote, target, null));
        var asPromote = await VerifyHandler().Handle(
            new VerifySecretOperationChallengeCommand(otherId, SecretChallengeTestContext.CorrectCode, _keyring.Admin, KeyringAction.Promote),
            CancellationToken.None);

        asPromote.IsError.Should().BeFalse();
        asPromote.Value.AffectedUsers.Should().Be(3, "a scheduled promote breaks only what outlives the overlap window");
        asPromote.Value.Details.Select(d => d.Code).Should().Contain(SecretRotationImpactCodes.UsersBeyondOverlap);
    }

    [Fact]
    public async Task TheExecutedEvent_NamesTheActionAndTheKey()
    {
        await _keyring.StageAsync();

        await _keyring.PromoteAsync();

        _keyring.Publisher.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e =>
                    e.Operation == SecretOperation.GenerateHmacKey && e.Action == KeyringAction.Promote && e.Target == 2),
                CancellationToken.None),
            Times.Once,
            "the trail names which keyring action ran and on which key, published past the request's cancellation");
    }

    [Fact]
    public async Task TheProcess_IssuesUnderTheNewRing_OnlyAfterItWasSaved()
    {
        await _keyring.StageAsync();
        var before = _keyring.Holder.Current;
        var failing = new KeyringTestHarnessService(_keyring, new SaveFailsSecretService(_keyring.Secrets));

        var result = await failing.ChangeAsync(
            _keyring.Request(KeyringAction.Promote, _keyring.Approve(KeyringAction.Promote)),
            (ring, change) => ring.Promote(change.NowUtc, change.DemotedKeyVerifyUntilUtc),
            precheck: null,
            CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.FileAccessFailed");
        _keyring.Holder.Current.Should().BeSameAs(before, "a ring the file does not hold would be lost at the next start");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task APromote_RequiresTheStagedKeyLoadedInThisProcess()
    {
        // Another worker staged key 2 into the file; this process never loaded it.
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                HmacKeyringFile.Write(
                    HmacKeyringFile.Read(secrets).Value.Stage(Convert.FromBase64String(KeyringTestHarness.KeyMaterial()), _keyring.Clock.UtcNow).Value,
                    secrets);
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        var approval = _keyring.Approve(KeyringAction.Promote);
        var result = await Promote().Handle(
            new PromoteHmacKeyCommand(false, approval, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.KeyringNoNextKey");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "the confirmation is not spent on a promote this process cannot make");

        var raised = await RequestHandler().Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateHmacKey, null, _keyring.Admin, null, KeyringAction.Promote),
            CancellationToken.None);
        raised.FirstError.Code.Should().Be("Secret.KeyringNoNextKey", "and no code is emailed for it");
    }

    [Fact]
    public async Task Retire_IsRefused_WhileWebhookKeysStoredUnderTheKeyAreInTheirGrace()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        _keyring.WebhookCounts = new Dictionary<int, int> { [1] = 2 };
        var approval = _keyring.Approve(KeyringAction.Retire, keyId: 1);

        var result = await new RetireHmacKeyCommandHandler(_keyring.Service, _keyring.WebhookKeys.Object).Handle(
            new RetireHmacKeyCommand(1, approval, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.KeyringKeyReferenced");
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "the confirmation is not spent on a retire that cannot happen");
        _keyring.ReadFileRing().Find(1).Should().NotBeNull();
    }

    [Fact]
    public async Task StageImport_OfAKeyTheRingHolds_IsRefusedBeforeTheApprovalIsSpent()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        var older = Convert.ToBase64String(_keyring.ReadFileRing().Find(1)!.Material.ToArray());
        var approval = _keyring.Approve(KeyringAction.StageImport, value: older);

        var result = await new StageHmacKeyCommandHandler(_keyring.Service, new SharedKeyMaterialGenerator()).Handle(
            new StageHmacKeyCommand(older, approval, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        result.FirstError.Description.Should().Contain("already HMAC key 1");
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "the confirmation is not spent on a stage that cannot happen");
        _keyring.ReadFileRing().Next.Should().BeNull();
    }

    [Fact]
    public async Task AFileRestoredFromBeforeTheRotation_IsRefused_AndTheProcessKeepsItsRing()
    {
        var beforeRotation = await File.ReadAllBytesAsync(_keyring.FilePath, CancellationToken.None);
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        // An older copy put back while the API runs: the file holds key 1 alone again.
        await File.WriteAllBytesAsync(_keyring.FilePath, beforeRotation, CancellationToken.None);
        var approval = _keyring.Approve(KeyringAction.Stage);

        var result = await new StageHmacKeyCommandHandler(_keyring.Service, new SharedKeyMaterialGenerator()).Handle(
            new StageHmacKeyCommand(null, approval, _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        result.FirstError.Description.Should().Contain("no longer holds HMAC key 2")
            .And.Contain("restart this one", "the usual cause is another process; restart before any copy")
            .And.Contain("never one from before a retire or an emergency");
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "the confirmation is not spent on a change that would drop a live key");
        _keyring.Holder.Current.Current.Id.Should().Be(2, "the process keeps issuing under the key it promoted");
        _keyring.ReadFileRing().Keys.Should().ContainSingle("nothing was written over the restored copy");
        (await _keyring.Service.ViewAsync(CancellationToken.None)).Value.InSync.Should().BeFalse();
    }

    [Fact]
    public async Task AFileWhoseRingWasErased_IsRefused_AsTheBootRefusesIt()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        // A build without the keyring saved the file: the ring is gone, the version and the
        // single key (key 2's material) stay.
        await _keyring.Secrets.UpdateAsync<Success>(
            secrets =>
            {
                secrets.HmacKeys.Clear();
                secrets.HmacCurrentKeyId = 0;
                secrets.HmacNextKeyId = 0;
                secrets.HmacLegacyCutoverUtc = null;
                secrets.HmacLegacyVerifyUntilUtc = null;
                return Result.Success;
            },
            overwriteBackup: false,
            CancellationToken.None);

        var view = await _keyring.Service.ViewAsync(CancellationToken.None);

        view.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        view.FirstError.Description.Should().Contain("no longer has one");
        SecretConfiguration.VersionWithRingBeyondKey1.Should().Be(
            Auth.Shared.Configuration.SecretFileVersions.HmacKeyringBeyondKey1,
            "the runtime reader and the boot read one marker");
    }

    [Fact]
    public async Task ARequestWithoutAnApproval_LearnsNothingAboutWhetherAKeyIsInTheRing()
    {
        var held = Convert.ToBase64String(_keyring.ReadFileRing().Current.Material.ToArray());

        var result = await new StageHmacKeyCommandHandler(_keyring.Service, new SharedKeyMaterialGenerator()).Handle(
            new StageHmacKeyCommand(held, Guid.NewGuid(), _keyring.Admin), CancellationToken.None);

        result.FirstError.Code.Should().Be("Secret.ChallengeNotApproved",
            "a key the ring holds and a key it does not are refused alike without a live approval");
    }

    [Fact]
    public void AKeyringDigest_IsNeverTheDigestOfAValueAnImportAccepts()
    {
        var value = KeyringTestHarness.KeyMaterial();

        // With '/' only, this preimage is itself valid key material (base64, at least 32
        // bytes), so an import alias confirmed with it would spend the staging approval.
        var slashed = $"keyring/hmac/StageImport/100\n{value}";
        SecretKeyMaterial.ValidateHmacKey(slashed).IsError.Should().BeFalse("the collision the ':' closes is real");

        KeyringStepUp.Digest(KeyringAction.StageImport, "100", value)
            .Should().NotBe(SecretPayloadDigest.Compute(slashed));
        SecretKeyMaterial.ValidateHmacKey($"keyring:hmac/StageImport/100\n{value}").IsError.Should().BeTrue();
    }

    [Fact]
    public async Task AnEmergency_LeavesNoBackupWhereTheLeakedKeysAreHealthy()
    {
        await _keyring.StageAsync();

        var result = await Promote().Handle(
            new PromoteHmacKeyCommand(true, _keyring.Approve(KeyringAction.PromoteEmergency), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        // The backup copy is the file's own fallback for a file that fails to decrypt: kept
        // as it was, it would bring every leaked key back, healthy and issuing.
        var backup = BackupRing(_keyring);
        backup.Current.Id.Should().Be(3);
        backup.Keys.Where(key => key.Id != 3).Should().OnlyContain(key => key.Compromised);
    }

    [Fact]
    public async Task ADiscard_LeavesNoMaterialOfTheDiscardedKeyInTheBackup()
    {
        await _keyring.StageAsync();
        var discarded = Convert.ToBase64String(_keyring.ReadFileRing().Next!.Material.ToArray());

        var result = await new DiscardStagedHmacKeyCommandHandler(_keyring.Service).Handle(
            new DiscardStagedHmacKeyCommand(_keyring.Approve(KeyringAction.DiscardNext), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        BackupRing(_keyring).Keys.Select(key => Convert.ToBase64String(key.Material.ToArray()))
            .Should().NotContain(discarded);
    }

    [Fact]
    public async Task TwoChangesInOneProcess_LeaveItOnTheRingSavedLast()
    {
        await _keyring.StageAsync();
        var gated = new HoldAfterFirstSaveService(_keyring.Secrets);
        var service = new KeyringTestHarnessService(_keyring, gated);

        // A promote saves, then is held after the file lock was released and before its
        // caller resumes: an emergency runs meanwhile, saves and finishes.
        var promote = service.ChangeAsync(
            _keyring.Request(KeyringAction.Promote, _keyring.Approve(KeyringAction.Promote)),
            (ring, change) => ring.Promote(change.NowUtc, change.DemotedKeyVerifyUntilUtc),
            precheck: null,
            CancellationToken.None);
        (await gated.FirstSaved.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        var emergency = await service.ChangeAsync(
            _keyring.Request(KeyringAction.PromoteEmergency, _keyring.Approve(KeyringAction.PromoteEmergency), material: KeyringTestHarness.KeyMaterial()),
            (ring, change) => ring.PromoteEmergency(change.Material!, change.NowUtc),
            precheck: null,
            CancellationToken.None);
        emergency.IsError.Should().BeFalse(string.Join("; ", emergency.ErrorsOrEmptyList.Select(e => e.Code)));
        gated.Resume.Release();
        (await promote).IsError.Should().BeFalse();

        _keyring.Holder.Current.Current.Id.Should().Be(3, "the emergency saved last");
        HmacKeyringFile.SameRing(_keyring.ReadFileRing(), _keyring.Holder.Current).Should().BeTrue();
    }

    [Fact]
    public async Task ARetireWhoseBackupOverwriteFails_IsReportedDone_AndTheProcessFollowsTheFile()
    {
        var protection = new FaultyProtectionProvider();
        using var keyring = new KeyringTestHarness(protection: protection);
        await keyring.StageAsync();
        await keyring.PromoteAsync();
        // The first write (the retire) lands; the second, which would overwrite the backup, fails.
        protection.FailAfter(1, new IOException("The process cannot access the file because it is being used by another process."));

        var result = await new RetireHmacKeyCommandHandler(keyring.Service, keyring.WebhookKeys.Object).Handle(
            new RetireHmacKeyCommand(1, keyring.Approve(KeyringAction.Retire, keyId: 1), keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse("the retire is in the file; only its backup copy was not overwritten");
        keyring.ReadFileRing().Find(1).Should().BeNull();
        keyring.Holder.Current.Find(1).Should().BeNull("the process holds what the file holds");
        keyring.Publisher.Verify(
            p => p.Publish(
                It.Is<SecretOperationExecutedEvent>(e => e.Action == KeyringAction.Retire && e.Target == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ASecretsFileThatCannotBeWritten_AnswersFileAccessFailed_NeverA500()
    {
        var protection = new FaultyProtectionProvider();
        using var keyring = new KeyringTestHarness(protection: protection);
        protection.FailAfter(0, new UnauthorizedAccessException("Access to the path is denied."));

        var staged = await keyring.Service.ChangeAsync(
            keyring.Request(KeyringAction.Stage, keyring.Approve(KeyringAction.Stage), material: KeyringTestHarness.KeyMaterial()),
            (ring, change) => ring.Stage(change.Material!, change.NowUtc),
            precheck: null,
            CancellationToken.None);

        staged.FirstError.Code.Should().Be("Secret.FileAccessFailed");
        keyring.Holder.Current.Next.Should().BeNull();
    }

    [Fact]
    public async Task ASnapshotThatFails_LeavesTheConfirmationUnspent()
    {
        await _keyring.StageAsync();
        _keyring.Impact
            .Setup(r => r.GetImpactAsync(It.IsAny<TimeSpan>(), It.IsAny<DateTime?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The impact query timed out."));
        var approval = _keyring.Approve(KeyringAction.Promote);

        var act = () => Promote().Handle(new PromoteHmacKeyCommand(false, approval, _keyring.Admin), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        _keyring.Challenges.ChallengeRepository.Verify(
            r => r.TryConsumeAsync(approval, It.IsAny<CancellationToken>()), Times.Never,
            "every read the change needs happens before the code is spent");
        _keyring.ReadFileRing().Current.Id.Should().Be(1);
    }

    [Fact]
    public async Task AFailureAfterTheSave_StillAnswersTheChangeAsDone()
    {
        _keyring.Publisher
            .Setup(p => p.Publish(It.IsAny<SecretOperationExecutedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The audit insert failed."));
        _keyring.WebhookKeys
            .Setup(r => r.CountLiveByHmacKeyAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The count timed out."));

        var staged = await _keyring.Service.ChangeAsync(
            _keyring.Request(KeyringAction.Stage, _keyring.Approve(KeyringAction.Stage), material: KeyringTestHarness.KeyMaterial()),
            (ring, change) => ring.Stage(change.Material!, change.NowUtc),
            precheck: null,
            CancellationToken.None);

        staged.IsError.Should().BeFalse("a durable change answered as failed is run a second time");
        staged.Value.NextKeyId.Should().Be(2);
        _keyring.Holder.Current.Next!.Id.Should().Be(2);
    }

    [Theory]
    [InlineData(20, 12, 20)]
    [InlineData(45, 12, 30)]
    [InlineData(3, 2, 7)]
    public async Task APromote_SizesTheOldKeysWindowFromTheLatestRefreshTokenAndSsoSession(int refreshDays, int ssoDays, int windowDays)
    {
        _keyring.Clock.Advance(TimeSpan.Zero);
        var now = _keyring.Clock.UtcNow;
        await _keyring.StageAsync();
        // The database answers UTC without a kind.
        _keyring.Snapshot = _keyring.Snapshot with
        {
            LatestRefreshTokenExpiresAt = DateTime.SpecifyKind(now.AddDays(refreshDays), DateTimeKind.Unspecified),
            LatestSsoSessionExpiresAt = DateTime.SpecifyKind(now.AddDays(ssoDays), DateTimeKind.Unspecified)
        };

        await _keyring.PromoteAsync();

        _keyring.ReadFileRing().Find(1)!.VerifyUntilUtc.Should().Be(now.AddDays(windowDays).AddSeconds(300));
    }

    [Fact]
    public async Task AReinstate_SizesTheDemotedKeysWindowTheSameWay()
    {
        _keyring.Clock.Advance(TimeSpan.Zero);
        var now = _keyring.Clock.UtcNow;
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        _keyring.Snapshot = _keyring.Snapshot with
        {
            LatestRefreshTokenExpiresAt = DateTime.SpecifyKind(now.AddDays(20), DateTimeKind.Unspecified),
            LatestSsoSessionExpiresAt = DateTime.SpecifyKind(now.AddDays(12), DateTimeKind.Unspecified)
        };

        var result = await new ReinstateHmacKeyCommandHandler(_keyring.Service).Handle(
            new ReinstateHmacKeyCommand(1, _keyring.Approve(KeyringAction.Reinstate, keyId: 1), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _keyring.ReadFileRing().Find(2)!.VerifyUntilUtc.Should().Be(now.AddDays(20).AddSeconds(300));
    }

    [Fact]
    public async Task TheAliasChallenges_KeepTodaysDigests_AndTheAliasesSpendThem()
    {
        // Stop condition 3: a console loaded before the keyring raises these challenges.
        var stored = new List<SecretOperationChallenge>();
        _keyring.Challenges.ChallengeRepository
            .Setup(r => r.CreateAsync(It.IsAny<SecretOperationChallenge>(), It.IsAny<CancellationToken>()))
            .Callback((SecretOperationChallenge c, CancellationToken _) => stored.Add(c));
        var value = KeyringTestHarness.KeyMaterial();

        await RequestHandler().Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.GenerateHmacKey, null, _keyring.Admin, null), CancellationToken.None);
        await RequestHandler().Handle(
            new RequestSecretOperationChallengeCommand(SecretOperation.ImportHmacKey, value, _keyring.Admin, null), CancellationToken.None);

        stored.Should().HaveCount(2);
        stored[0].PayloadHash.Should().BeNull("a generate binds no payload, as before the keyring");
        stored[1].PayloadHash.Should().Be(SecretPayloadDigest.Compute(value), "an import binds the digest of its value");

        var generate = Guid.NewGuid();
        _keyring.Challenges.WithApproval(generate, SecretOperation.GenerateHmacKey, _keyring.Admin, stored[0].PayloadHash);
        (await new GenerateHmacKeyCommandHandler(_keyring.Service, new SharedKeyMaterialGenerator()).Handle(
            new GenerateHmacKeyCommand(generate, _keyring.Admin), CancellationToken.None)).IsError.Should().BeFalse();

        var import = Guid.NewGuid();
        _keyring.Challenges.WithApproval(import, SecretOperation.ImportHmacKey, _keyring.Admin, stored[1].PayloadHash);
        (await new ImportHmacKeyCommandHandler(
                _keyring.Service, TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = "Certificate" }))
            .Handle(new ImportHmacKeyCommand(value, import, _keyring.Admin), CancellationToken.None)).IsError.Should().BeFalse();
    }

    private static HmacKeyring BackupRing(KeyringTestHarness keyring)
    {
        var backup = new DpapiSecretService(
            keyring.Protection,
            Options.Create(new SecretManagementSettings { SecretFilePath = keyring.FilePath + ".bak" }),
            NullLogger<DpapiSecretService>.Instance);
        return HmacKeyringFile.Read(backup.LoadSecretsAsync(CancellationToken.None).GetAwaiter().GetResult()).Value;
    }

    [Fact]
    public async Task Retire_SavesTwice_SoTheBackupLosesTheKeyToo()
    {
        await _keyring.StageAsync();
        await _keyring.PromoteAsync();
        var material1 = Convert.ToBase64String(_keyring.ReadFileRing().Find(1)!.Material);

        var result = await new RetireHmacKeyCommandHandler(_keyring.Service, _keyring.WebhookKeys.Object).Handle(
            new RetireHmacKeyCommand(1, _keyring.Approve(KeyringAction.Retire, keyId: 1), _keyring.Admin), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _keyring.ReadFileRing().Find(1).Should().BeNull();
        var backup = _keyring.Protection.CreateProtector("AuthSystem.SecretManagement.v1")
            .Unprotect(File.ReadAllBytes(_keyring.FilePath + ".bak"));
        System.Text.Encoding.UTF8.GetString(backup).Should().NotContain(material1);
    }

    [Fact]
    public async Task TheKeyringView_NeverCarriesKeyMaterial()
    {
        await _keyring.StageAsync();
        var ring = _keyring.ReadFileRing();

        var view = await new GetHmacKeyringQueryHandler(_keyring.Service).Handle(new GetHmacKeyringQuery(), CancellationToken.None);

        var json = JsonSerializer.Serialize(view.Value);
        foreach (var key in ring.Keys)
        {
            json.Should().NotContain(Convert.ToBase64String(key.Material), $"key {key.Id}'s material must never leave the server");
        }

        view.Value.Keys.Select(k => (k.Id, k.State, k.LoadedInProcess))
            .Should().Equal((1, HmacKeyState.Current, true), (2, HmacKeyState.Next, true));
    }

    private RequestSecretOperationChallengeCommandHandler RequestHandler() => new(
        _keyring.Challenges.Service,
        _keyring.Service,
        TestHelpers.CreateOptions(new SecretManagementSettings { StorageMode = "Certificate" }),
        _keyring.Publisher.Object,
        _keyring.Clock,
        new Mock<ILogger<RequestSecretOperationChallengeCommandHandler>>().Object);

    private VerifySecretOperationChallengeCommandHandler VerifyHandler() => new(
        _keyring.Challenges.Service,
        _keyring.Service,
        _keyring.Impact.Object,
        TestHelpers.CreateOptions(new JwtSettings()),
        _keyring.Clock,
        new Mock<ILogger<VerifySecretOperationChallengeCommandHandler>>().Object);

    // An unanswered challenge bound to a keyring digest, answered with the correct code.
    private Guid OpenChallenge(string payloadHash)
    {
        var challengeId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var challenge = new SecretOperationChallenge(
            challengeId, _keyring.Admin, SecretOperation.GenerateHmacKey, payloadHash, SecretChallengeTestContext.CodeHash,
            expiresAt: now.AddMinutes(15), verifiedAt: null, approvalExpiresAt: null, usedAt: null,
            attemptCount: 0, ipAddress: null, createdAt: now);
        _keyring.Challenges.ChallengeRepository
            .Setup(r => r.GetByIdAsync(challengeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        _keyring.Challenges.ChallengeRepository
            .Setup(r => r.TryRegisterAttemptAsync(challengeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return challengeId;
    }

    /// <summary>The harness's service over another secret service.</summary>
    private sealed class KeyringTestHarnessService(KeyringTestHarness harness, IDpapiSecretService secrets)
    {
        private readonly HmacKeyringChangeService _service = new(
            secrets,
            harness.Holder,
            harness.Challenges.Service,
            harness.Impact.Object,
            harness.WebhookKeys.Object,
            harness.Publisher.Object,
            harness.StorageMode,
            harness.Clock,
            new Mock<ILogger<HmacKeyringChangeService>>().Object);

        public Task<ErrorOr<HmacKeyringDto>> ChangeAsync(
            KeyringChangeRequest request,
            Func<HmacKeyring, KeyringChangeContext, ErrorOr<HmacKeyring>> apply,
            Func<HmacKeyring, DateTime, CancellationToken, Task<ErrorOr<Success>>>? precheck,
            CancellationToken cancellationToken) =>
            _service.ChangeAsync(request, apply, precheck, cancellationToken);
    }

    /// <summary>A secret service whose save fails after the change was computed, as a full disk would.</summary>
    private sealed class SaveFailsSecretService(DpapiSecretService inner) : IDpapiSecretService
    {
        public bool SecretFileExists() => inner.SecretFileExists();
        public string GetSecretFilePath() => inner.GetSecretFilePath();
        public Task<SecretConfiguration> LoadSecretsAsync(CancellationToken cancellationToken) => inner.LoadSecretsAsync(cancellationToken);

        public async Task<ErrorOr<T>> UpdateAsync<T>(
            Func<SecretConfiguration, ErrorOr<T>> change, bool overwriteBackup, CancellationToken cancellationToken, Action<T>? onSaved = null)
        {
            change(await inner.LoadSecretsAsync(cancellationToken));
            throw new IOException("There is not enough space on the disk.");
        }

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveSecretAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> GenerateRsaKeyPairAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> GenerateGatewayTokenAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KeyGenerationResult> GenerateMissingKeysAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ImportRsaKeyPairAsync(string privateKeyPem, string publicKeyPem, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ImportGatewayTokenAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SecretStatusResult> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// The real secret service, whose first change is held after the file lock was released
    /// and before its caller resumes: what a descheduled continuation looks like.
    /// </summary>
    private sealed class HoldAfterFirstSaveService(DpapiSecretService inner) : IDpapiSecretService
    {
        private int _saves;

        public SemaphoreSlim FirstSaved { get; } = new(0, 1);

        public SemaphoreSlim Resume { get; } = new(0, 1);

        public bool SecretFileExists() => inner.SecretFileExists();
        public string GetSecretFilePath() => inner.GetSecretFilePath();
        public Task<SecretConfiguration> LoadSecretsAsync(CancellationToken cancellationToken) => inner.LoadSecretsAsync(cancellationToken);

        public async Task<ErrorOr<T>> UpdateAsync<T>(
            Func<SecretConfiguration, ErrorOr<T>> change, bool overwriteBackup, CancellationToken cancellationToken, Action<T>? onSaved = null)
        {
            var result = await inner.UpdateAsync(change, overwriteBackup, cancellationToken, onSaved);
            if (Interlocked.Increment(ref _saves) == 1)
            {
                FirstSaved.Release();
                await Resume.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }

            return result;
        }

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

    /// <summary>A protector whose protect fails on command: a disk that refuses one write.</summary>
    private sealed class FaultyProtectionProvider : IDataProtectionProvider
    {
        private readonly EphemeralDataProtectionProvider _inner = new();
        private int _protectsBeforeFailure = -1;
        private Exception? _failure;

        /// <summary>Lets <paramref name="protects"/> more writes through, then fails the next one.</summary>
        public void FailAfter(int protects, Exception failure)
        {
            _failure = failure;
            _protectsBeforeFailure = protects;
        }

        public IDataProtector CreateProtector(string purpose) => new Protector(this, _inner.CreateProtector(purpose));

        private sealed class Protector(FaultyProtectionProvider owner, IDataProtector inner) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => new Protector(owner, inner.CreateProtector(purpose));

            public byte[] Protect(byte[] plaintext)
            {
                if (owner._protectsBeforeFailure == 0)
                {
                    owner._protectsBeforeFailure = -1;
                    throw owner._failure!;
                }

                if (owner._protectsBeforeFailure > 0)
                {
                    owner._protectsBeforeFailure--;
                }

                return inner.Protect(plaintext);
            }

            public byte[] Unprotect(byte[] protectedData) => inner.Unprotect(protectedData);
        }
    }
}
