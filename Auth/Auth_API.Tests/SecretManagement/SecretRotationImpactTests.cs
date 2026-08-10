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
        ActiveWebhookKeys = 6
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

    [Theory]
    [InlineData(SecretOperation.GenerateHmacKey)]
    [InlineData(SecretOperation.ImportHmacKey)]
    public void HmacRotation_ReportsEveryCredentialClassHashedWithTheKey(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.AffectedUsers.Should().Be(37);
        impact.Details.Select(d => d.Code).Should().BeEquivalentTo(
        [
            SecretRotationImpactCodes.UsersSignedOut,
            SecretRotationImpactCodes.UsersWithSsoSessions,
            SecretRotationImpactCodes.PendingPasswordResets,
            SecretRotationImpactCodes.PendingTwoFactorChallenges,
            SecretRotationImpactCodes.ActiveWebhookKeys
        ]);
    }

    [Theory]
    [InlineData(SecretOperation.GenerateHmacKey)]
    [InlineData(SecretOperation.ImportHmacKey)]
    public void HmacRotation_SurfacesWebhookKeys(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.Details.Should().Contain(d =>
                d.Code == SecretRotationImpactCodes.ActiveWebhookKeys && d.Count == 6,
            "webhook keys share this HMAC key, and nothing in the old warning said so");
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
    [InlineData(SecretOperation.GenerateHmacKey)]
    [InlineData(SecretOperation.GenerateGatewayToken)]
    [InlineData(SecretOperation.ImportRsaKey)]
    [InlineData(SecretOperation.ImportHmacKey)]
    [InlineData(SecretOperation.ImportGatewayToken)]
    public void EveryOperation_SaysTheChangeNeedsARestart(SecretOperation operation)
    {
        var impact = Build(operation);

        impact.Operation.Should().Be(operation);
        impact.ApprovalExpiresAt.Should().Be(ApprovalExpiry);
        impact.RequiresApiRestart.Should().BeTrue(
            "the running process captured the old key at boot, so nothing changes for users until it recycles");
        impact.Details.Should().NotBeEmpty();
    }
}
