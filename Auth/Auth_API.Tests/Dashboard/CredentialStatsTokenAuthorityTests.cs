using Auth.Application.Features.Dashboard.GetCredentialStats;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Dashboard;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Dashboard;

/// <summary>
/// S08 T11: credential-stats has no endpoint gate of its own, so the handler is the
/// gate — and it must not hand a session whose platform authority is withheld (no
/// second factor yet) what the token was minted without. Visibility is the token's
/// claim AND the live grant; without the claim the database is not asked.
/// </summary>
public class CredentialStatsTokenAuthorityTests
{
    private static readonly Guid Caller = Guid.NewGuid();

    private readonly Mock<IDashboardStatsRepository> _repository = new();
    private readonly Mock<IPermissionChecker> _checker = new();

    public CredentialStatsTokenAuthorityTests()
    {
        _repository.Setup(r => r.GetCredentialStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialStatsSnapshot
            {
                HorizonDays = 14,
                ApiKeys = new CredentialExpiryBucket { ExpiringCount = 2, SoonestExpiresAt = null, TotalActive = 5 },
                WebhookKeys = new CredentialExpiryBucket { ExpiringCount = 1, SoonestExpiresAt = null, TotalActive = 3 }
            });
    }

    private Task<ErrorOr.ErrorOr<Auth.Application.DTOs.CredentialStatsDto>> Handle(bool tokenApiKeys, bool tokenWebhookKeys) =>
        new GetCredentialStatsQueryHandler(_repository.Object, _checker.Object, Mock.Of<ILogger<GetCredentialStatsQueryHandler>>())
            .Handle(
                new GetCredentialStatsQuery(14)
                {
                    RequestedBy = Caller,
                    TokenGrantsApiKeysRead = tokenApiKeys,
                    TokenGrantsWebhookKeysRead = tokenWebhookKeys
                },
                CancellationToken.None);

    private void LiveGrant(string permission, bool granted) =>
        _checker.Setup(c => c.HasPermissionAsync(Caller, permission, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(granted);

    [Fact]
    public async Task Handle_TokenWithoutTheClaim_SeesNothing_AndTheDatabaseIsNotAsked()
    {
        // A platform administrator whose permissions are withheld still holds them
        // live: the live check alone would hand the buckets back.
        LiveGrant(PermissionCodes.ApiKeys.Read, true);
        LiveGrant(PermissionCodes.WebhookKeys.Read, true);

        var result = await Handle(tokenApiKeys: false, tokenWebhookKeys: false);

        result.Value.ApiKeys.Should().BeNull();
        result.Value.WebhookKeys.Should().BeNull();
        _checker.VerifyNoOtherCalls();
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ClaimButRevokedLive_SeesNothing()
    {
        LiveGrant(PermissionCodes.ApiKeys.Read, false);
        LiveGrant(PermissionCodes.WebhookKeys.Read, false);

        var result = await Handle(tokenApiKeys: true, tokenWebhookKeys: true);

        result.Value.ApiKeys.Should().BeNull("a permission revoked since the token was minted is not honoured");
        result.Value.WebhookKeys.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ClaimAndLiveGrant_SeesTheFamily()
    {
        LiveGrant(PermissionCodes.ApiKeys.Read, true);
        LiveGrant(PermissionCodes.WebhookKeys.Read, true);

        var result = await Handle(tokenApiKeys: true, tokenWebhookKeys: false);

        result.Value.ApiKeys.Should().NotBeNull();
        result.Value.WebhookKeys.Should().BeNull("the token does not carry webhookkeys:read");
        _checker.Verify(c => c.HasPermissionAsync(Caller, PermissionCodes.WebhookKeys.Read, null, It.IsAny<CancellationToken>()),
            Times.Never, "the database is asked only about what the token carries");
    }
}
