using Auth.Domain.Enums;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 SEC-5: a candidate set carries the purpose it was computed for, and a repository
/// takes its fingerprints only for the purpose its column holds. Until the first rotation
/// every purpose computes the same legacy fingerprint of the same value, so the mistake
/// these tests catch (SSO-session candidates handed to the refresh-token lookup) works
/// until the rotation, and then fails every credential of the kind.
/// </summary>
public class CredentialHashCandidatesPurposeTests
{
    [Fact]
    public async Task IdpSessionCandidates_PassedToTheRefreshTokenLookup_Throw()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var candidates = FakeCredentialHasher.CandidatesOf(CredentialPurpose.IdpSession, "hash");

        var lookup = () => new RefreshTokenRepository(db).GetByTokenHashAsync(candidates, CancellationToken.None);

        (await lookup.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*IdpSession*RefreshToken*");
        db.Commands.Should().BeEmpty("the refused lookup never reaches the database");
    }

    [Fact]
    public async Task CandidatesOfTheColumnsPurpose_AreLookedUp()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new RefreshTokenRepository(db).GetByTokenHashAsync(
            FakeCredentialHasher.CandidatesOf(CredentialPurpose.RefreshToken, "hash"), CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("IN (@Hashes1)", "Dapper expands the candidates into the IN list");
    }

    [Fact]
    public void TheHasher_IssuesCandidatesOfThePurposeItWasAskedFor()
    {
        var hasher = new Auth.Infrastructure.Security.KeyringCredentialHasher(
            new Auth.Application.Security.Keyring.HmacKeyringHolder(KeyringTestRings.SingleKey()), TimeProvider.System);

        var candidates = hasher.Candidates(CredentialPurpose.Invitation, "token");

        candidates.Purpose.Should().Be(CredentialPurpose.Invitation);
        candidates.ValuesFor(CredentialPurpose.Invitation).Should().Equal(candidates.Values);
        candidates.Invoking(c => c.ValuesFor(CredentialPurpose.PasswordReset)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equality_IsOverTheFingerprints_SoAMockSetupMatchesWhateverThePurpose()
    {
        FakeCredentialHasher.CandidatesOf(CredentialPurpose.WebhookKey, "a", "b")
            .Should().Be(FakeCredentialHasher.CandidatesOf("a", "b"));
    }
}
