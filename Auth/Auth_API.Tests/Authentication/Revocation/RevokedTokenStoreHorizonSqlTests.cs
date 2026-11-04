using System.Text.RegularExpressions;
using Auth.Infrastructure.Persistence;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// X04, RV2 (c): in the durable store a user-level row counts until
/// <c>max(ExpiresAt, EffectiveAt + RevocationHorizon.User)</c>, in the read the startup load and
/// the periodic re-read use, and in the purge. Rows written before X04 carry a one-hour expiry;
/// without the user branch the read skipped them after the hour and the purge deleted them, so a
/// restart accepted every token they had revoked. The test project has no database, so the SQL
/// text is the unit under test (as in <c>UserHardDeleteSqlTests</c>).
/// </summary>
/// <remarks>Deliberate breaks, each seen red: drop the OR branch of the read, or the AND NOT of
/// the purge.</remarks>
public class RevokedTokenStoreHorizonSqlTests
{
    private static string Flat(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    [Fact]
    public void TheRead_KeepsAUserRowUntilTheHorizon_BehindTheSeekableExpiryBound()
    {
        Flat(RevokedTokenStore.GetActiveSql).Should().EndWith(
            "WHERE [ExpiresAt] > @Cut AND ([ExpiresAt] > @Now OR ([RevocationType] = @UserRevocation AND [EffectiveAt] > @Cut))");
    }

    [Fact]
    public void ThePurge_SparesAUserRowUntilTheHorizon()
    {
        Flat(RevokedTokenStore.PurgeSql).Should().Be(
            "DELETE FROM [dbo].[RevokedTokens] WHERE [ExpiresAt] <= @OlderThan AND NOT ([RevocationType] = @UserRevocation AND [EffectiveAt] > @Cut)");
    }

    [Fact]
    public void BothStatements_TakeTheCutFromTheHorizon_AndTheUserTypeFromTheEnum()
    {
        var source = File.ReadAllText(Path.Combine(
            Infrastructure.ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "RevokedTokenStore.cs"));

        Regex.Matches(source, @"Cut = (now|olderThan) - RevocationHorizon\.User").Count.Should().Be(2);
        Regex.Matches(source, @"UserRevocation = \(byte\)RevocationType\.User").Count.Should().Be(2);
    }
}
