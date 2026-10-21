using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Secrets;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// The blast-radius report shown immediately before a key is rotated.
/// </summary>
/// <remarks>
/// The single warning this replaced ("regenerating signing keys invalidates all
/// existing tokens") was wrong for two of the three keys. These tests pin the
/// distinctions: RSA forces a token refresh but signs nobody out, HMAC signs
/// everybody out and takes four other credential classes with it, and the
/// gateway token invalidates no user credential at all.
/// </remarks>
public class SecretRotationImpactTests
{
    private static readonly SecretRotationImpactSnapshot Snapshot = new()
    {
        UsersWithLiveAccessTokens = 11,
        UsersWithActiveSessions = 42,
        UsersWithActiveRefreshTokens = 37,
        UsersWithActiveIdpSessions = 9,
        PendingPasswordResets = 4,
        PendingTwoFactorChallenges = 2,
        ActiveWebhookKeys = 6,
        PendingInvitations = 5,
        PendingAuthorizationCodes = 3,
        PendingAuthorizationRequests = 1,
        PendingRegistrations = 7,
        PendingEmailCodes = 8,
        UsersWithRefreshTokensBeyondOverlap = 13,
        UsersWithSsoSessionsBeyondOverlap = 4
    };

    private static readonly DateTime ApprovalExpiry = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    private static SecretRotationImpactDto Build(SecretOperation operation) =>
        SecretRotationImpact.Build(operation, Snapshot, ApprovalExpiry);

    [Theory]
    [InlineData(SecretOperation.GenerateRsaKey)]
    [InlineData(SecretOperation.ImportRsaKey)]
    public void RsaRotation_CountsEveryoneWhoWillMeetARejectedRequest(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.AffectedUsers.Should().Be(42);
        impact.Details.Should().HaveCount(2);
        impact.Details.Should().Contain(d =>
            d.Code == SecretRotationImpactCodes.UsersWithLiveAccessTokens && d.Count == 11);
        impact.Details.Should().Contain(d =>
            d.Code == SecretRotationImpactCodes.UsersWithActiveSessions && d.Count == 42);
    }

    [Theory]
    [InlineData(SecretOperation.GenerateRsaKey)]
    [InlineData(SecretOperation.ImportRsaKey)]
    public void RsaRotation_DoesNotClaimToBreakRefreshTokensOrResetLinks(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.Details.Should().NotContain(d => d.Code == SecretRotationImpactCodes.UsersSignedOut,
            "refresh tokens are opaque and unsigned — an RSA rotation is a forced refresh, not a sign-out");
        impact.Details.Should().NotContain(d =>
            d.Code == SecretRotationImpactCodes.PendingPasswordResets);
        impact.Details.Should().NotContain(d =>
            d.Code == SecretRotationImpactCodes.ActiveWebhookKeys);
        impact.RequiresGatewayReconfiguration.Should().BeFalse();
    }

    // The generate/import HMAC aliases are emergencies (S11): they cost exactly what a
    // keyring emergency costs.
    [Theory]
    [InlineData(SecretOperation.GenerateHmacKey, null)]
    [InlineData(SecretOperation.ImportHmacKey, null)]
    [InlineData(SecretOperation.GenerateHmacKey, KeyringAction.PromoteEmergency)]
    public void HmacEmergency_ReportsEveryCredentialClassHashedWithTheKey(SecretOperation operation, KeyringAction? action)
    {
        var impact = SecretRotationImpact.Build(operation, action, Snapshot, ApprovalExpiry);

        impact.AffectedUsers.Should().Be(37);
        impact.Details.Select(d => (d.Code, d.Count)).Should().BeEquivalentTo(
        [
            (SecretRotationImpactCodes.UsersSignedOut, 37),
            (SecretRotationImpactCodes.UsersWithSsoSessions, 9),
            (SecretRotationImpactCodes.PendingPasswordResets, 4),
            (SecretRotationImpactCodes.PendingInvitations, 5),
            (SecretRotationImpactCodes.PendingTwoFactorChallenges, 2),
            (SecretRotationImpactCodes.PendingEmailCodes, 8),
            (SecretRotationImpactCodes.PendingRegistrations, 7),
            (SecretRotationImpactCodes.PendingAuthorizations, 4),
            (SecretRotationImpactCodes.WebhookKeysInGrace, 6)
        ]);
        impact.RequiresApiRestart.Should().BeFalse("the keyring applies a change in this process as soon as it is saved");
    }

    [Theory]
    [InlineData(SecretOperation.GenerateHmacKey)]
    [InlineData(SecretOperation.ImportHmacKey)]
    public void HmacEmergency_SaysWebhookKeysKeepTheirGrace_InsteadOfFailing(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.Details.Should().Contain(d => d.Code == SecretRotationImpactCodes.WebhookKeysInGrace && d.Count == 6);
        impact.Details.Should().NotContain(d => d.Code == SecretRotationImpactCodes.ActiveWebhookKeys,
            "webhook keys keep validating for 90 days after an HMAC emergency; none stops now");
    }

    [Theory]
    [InlineData(KeyringAction.Stage)]
    [InlineData(KeyringAction.StageImport)]
    [InlineData(KeyringAction.DiscardNext)]
    public void KeyringChange_ThatIssuesNothing_BreaksNothing(KeyringAction action)
    {
        var impact = SecretRotationImpact.Build(KeyringStepUp.OperationOf(action), action, Snapshot, ApprovalExpiry);

        impact.AffectedUsers.Should().Be(0);
        impact.Details.Should().BeEmpty("a staged key only verifies, and a discarded one never issued");
        impact.RequiresApiRestart.Should().BeFalse();
    }

    [Theory]
    [InlineData(KeyringAction.Promote)]
    [InlineData(KeyringAction.Reinstate)]
    public void ScheduledRotation_CountsOnlyWhatOutlivesTheOverlapWindow(KeyringAction action)
    {
        var impact = SecretRotationImpact.Build(SecretOperation.GenerateHmacKey, action, Snapshot, ApprovalExpiry);

        impact.AffectedUsers.Should().Be(13, "only a refresh token that outlives the 30-day cap, and stays idle, signs in again");
        impact.Details.Select(d => (d.Code, d.Count)).Should().BeEquivalentTo(
        [
            (SecretRotationImpactCodes.UsersBeyondOverlap, 13),
            (SecretRotationImpactCodes.SsoSessionsBeyondOverlap, 4)
        ]);
        impact.RequiresApiRestart.Should().BeFalse();
    }

    [Fact]
    public void Retire_CountsWhatTheKeyStillVerifies_WithoutWebhookKeys()
    {
        var impact = SecretRotationImpact.Build(SecretOperation.GenerateHmacKey, KeyringAction.Retire, Snapshot, ApprovalExpiry);

        impact.AffectedUsers.Should().Be(37);
        impact.Details.Should().NotContain(d => d.Code == SecretRotationImpactCodes.WebhookKeysInGrace,
            "a retire is refused while any webhook key still needs the key");
        impact.Details.Should().Contain(d => d.Code == SecretRotationImpactCodes.PendingInvitations && d.Count == 5);
    }

    [Theory]
    [InlineData(SecretOperation.GenerateGatewayToken)]
    [InlineData(SecretOperation.ImportGatewayToken)]
    public void GatewayRotation_IsAnOutageNotACredentialLoss(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.RequiresGatewayReconfiguration.Should().BeTrue();
        impact.AffectedUsers.Should().Be(42, "everyone with a live session is locked out meanwhile");
        impact.Details.Select(d => d.Code).Should().BeEquivalentTo(
            [SecretRotationImpactCodes.UsersWithActiveSessions]);
        impact.Details.Should().NotContain(d => d.Code == SecretRotationImpactCodes.UsersSignedOut,
            "no user credential is invalidated by a gateway token change");
    }

    [Theory]
    [InlineData(SecretOperation.GenerateRsaKey)]
    [InlineData(SecretOperation.GenerateGatewayToken)]
    [InlineData(SecretOperation.ImportRsaKey)]
    [InlineData(SecretOperation.ImportGatewayToken)]
    public void EveryOtherKeyOperation_SaysTheChangeNeedsARestart(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.Operation.Should().Be(operation);
        impact.ApprovalExpiresAt.Should().Be(ApprovalExpiry);
        impact.RequiresApiRestart.Should().BeTrue(
            "the running process captured the old key at boot, so nothing changes for users until it recycles");
        impact.Details.Should().NotBeEmpty();
    }
}
