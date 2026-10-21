using System.Text.RegularExpressions;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Security.Keyring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// S11 (fact 5): every repository that finds a row by a presented credential sends
/// EVERY candidate fingerprint to SQL — a value stored under the legacy form before a
/// rotation, and one stored under the new key's subkey after it, are both found.
/// The candidates here are real: key 2's subkey first, key 1's legacy form second.
/// </summary>
public class CredentialCandidateLookupSqlTests
{
    private static readonly DateTime Now = KeyringTestRings.Cutover.AddMinutes(1);

    private static CredentialHashCandidates TwoCandidates(CredentialPurpose purpose, out string subkey, out string legacy)
    {
        var hasher = TestCredentialHashers.Over(KeyringTestRings.Rotated(), new FixedTimeProvider(Now));
        var candidates = hasher.Candidates(purpose, "presented");
        candidates.Values.Should().HaveCount(2, "the rotated ring accepts the new key and the legacy form");
        subkey = candidates.Values[0];
        legacy = candidates.Values[1];
        legacy.Should().Be(TestCredentialHashers.Over(KeyringTestRings.SingleKey()).Hash(purpose, "presented"));
        return candidates;
    }

    private static void ShouldSendBoth(RecordingDbConnectionFactory db, string column, string parameter, string subkey, string legacy)
    {
        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");
        sql.Should().Contain($"{column} IN (@{parameter}1,@{parameter}2)");
        db.LastCommand.Parameters[$"{parameter}1"].Should().Be(subkey);
        db.LastCommand.Parameters[$"{parameter}2"].Should().Be(legacy);
    }

    [Fact]
    public async Task RefreshTokens_AreLookedUpByEveryCandidate()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var candidates = TwoCandidates(CredentialPurpose.RefreshToken, out var subkey, out var legacy);

        await new RefreshTokenRepository(db).GetByTokenHashAsync(candidates, CancellationToken.None);

        ShouldSendBoth(db, "[TokenHash]", "Hashes", subkey, legacy);
    }

    [Fact]
    public async Task TheReplayGracePointer_IsMatchedExactlyAsStored()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var pointer = TestHelpers.CreateRefreshToken(replacedByTokenHash: "stored-value").ReplacementTokenHash!;

        await new RefreshTokenRepository(db).GetByStoredTokenHashAsync(pointer, CancellationToken.None);

        Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ").Should().Contain("WHERE [TokenHash] = @StoredHash");
        db.LastCommand.Parameters["StoredHash"].Should().Be("stored-value");
    }

    [Fact]
    public async Task IdpSessions_AreLookedUpByEveryCandidate_LiveAndIncludingRevoked()
    {
        var candidates = TwoCandidates(CredentialPurpose.IdpSession, out var subkey, out var legacy);

        var live = new RecordingDbConnectionFactory(affectedRows: 0);
        await new IdpSessionRepository(live).GetByTokenHashAsync(candidates, CancellationToken.None);
        ShouldSendBoth(live, "[TokenHash]", "Hashes", subkey, legacy);

        var any = new RecordingDbConnectionFactory(affectedRows: 0);
        await new IdpSessionRepository(any).GetByTokenHashIncludingRevokedAsync(candidates, CancellationToken.None);
        ShouldSendBoth(any, "[TokenHash]", "Hashes", subkey, legacy);
    }

    [Fact]
    public async Task RevokingAllButTheCallersSession_SparesEveryCandidate()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 3);
        var candidates = TwoCandidates(CredentialPurpose.IdpSession, out var subkey, out var legacy);

        await new IdpSessionRepository(db).RevokeAllForUserExceptAsync(Guid.NewGuid(), candidates, CancellationToken.None);

        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");
        sql.Should().Contain("(@HasExcept = 0 OR [TokenHash] NOT IN (@ExceptHashes1,@ExceptHashes2))");
        db.LastCommand.Parameters["HasExcept"].Should().Be(1);
        db.LastCommand.Parameters["ExceptHashes1"].Should().Be(subkey);
        db.LastCommand.Parameters["ExceptHashes2"].Should().Be(legacy);
    }

    [Fact]
    public async Task RevokingAllSessions_WithNothingToSpare_SendsAnEmptyListDapperCanRun()
    {
        // Trap 3: with no session to spare the list is empty. Dapper writes it as an empty
        // sub-select, which SQL Server accepts; @HasExcept = 0 short-circuits the NOT IN.
        var db = new RecordingDbConnectionFactory(affectedRows: 3);

        await new IdpSessionRepository(db).RevokeAllForUserAsync(Guid.NewGuid(), CancellationToken.None);

        var sql = Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ");
        sql.Should().Contain("(@HasExcept = 0 OR [TokenHash] NOT IN (SELECT @ExceptHashes WHERE 1 = 0))");
        db.LastCommand.Parameters["HasExcept"].Should().Be(0);
    }

    [Fact]
    public async Task AuthorizationCodes_AreConsumedAndReadByEveryCandidate()
    {
        var candidates = TwoCandidates(CredentialPurpose.AuthorizationCode, out var subkey, out var legacy);

        var consumePublic = new RecordingDbConnectionFactory(affectedRows: 0);
        await new AuthorizationCodeRepository(consumePublic).ConsumeByCodeHashAsync(candidates, CancellationToken.None);
        ShouldSendBoth(consumePublic, "[CodeHash]", "Hashes", subkey, legacy);

        var consumeForClient = new RecordingDbConnectionFactory(affectedRows: 0);
        await new AuthorizationCodeRepository(consumeForClient).ConsumeForClientAsync(candidates, Guid.NewGuid(), CancellationToken.None);
        ShouldSendBoth(consumeForClient, "[CodeHash]", "Hashes", subkey, legacy);

        var read = new RecordingDbConnectionFactory(affectedRows: 0);
        await new AuthorizationCodeRepository(read).GetByCodeHashAsync(candidates, CancellationToken.None);
        ShouldSendBoth(read, "[CodeHash]", "Hashes", subkey, legacy);
    }

    [Fact]
    public async Task ParkedAuthorizeRequests_AreResumedByEveryCandidate()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var candidates = TwoCandidates(CredentialPurpose.AuthorizationRequestHandle, out var subkey, out var legacy);

        await new AuthorizationRequestRepository(db, NullLogger<AuthorizationRequestRepository>.Instance)
            .GetLiveByHandleHashAsync(candidates, Now, CancellationToken.None);

        ShouldSendBoth(db, "[HandleHash]", "Hashes", subkey, legacy);
    }

    [Fact]
    public async Task PasswordResetLinks_AreLookedUpByEveryCandidate()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var candidates = TwoCandidates(CredentialPurpose.PasswordReset, out var subkey, out var legacy);

        await new PasswordResetTokenRepository(db).GetByTokenHashAsync(candidates, CancellationToken.None);

        ShouldSendBoth(db, "[TokenHash]", "Hashes", subkey, legacy);
    }

    [Fact]
    public async Task TwoFactorChallenges_AreLookedUpByEveryCandidate()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var candidates = TwoCandidates(CredentialPurpose.TwoFactorChallenge, out var subkey, out var legacy);

        await new TwoFactorChallengeRepository(db).GetByTokenHashAsync(candidates, CancellationToken.None);

        ShouldSendBoth(db, "[TokenHash]", "Hashes", subkey, legacy);
    }

    [Fact]
    public async Task Invitations_AreLookedUpByEveryCandidate()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var candidates = TwoCandidates(CredentialPurpose.Invitation, out var subkey, out var legacy);

        await new OrganizationRepository(db).GetInvitationByTokenHashAsync(candidates, CancellationToken.None);

        ShouldSendBoth(db, "[Token]", "Hashes", subkey, legacy);
    }

    [Fact]
    public async Task WebhookKeys_AreLookedUpByEveryCandidate_PrefixedAndLegacy()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var candidates = TwoCandidates(CredentialPurpose.WebhookKey, out var subkey, out var legacy);
        subkey.Should().StartWith("hk2$");

        await new WebhookKeyRepository(db).GetByHashAsync(candidates, CancellationToken.None);

        ShouldSendBoth(db, "k.[KeyHash]", "Hashes", subkey, legacy);
    }
}
