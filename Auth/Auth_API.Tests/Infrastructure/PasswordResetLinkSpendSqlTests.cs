using System.Text.RegularExpressions;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// REVIEW-FIXES 150 F14 (OI-31): a password-reset link is spent by one conditional statement whose
/// row count is read, so of two submissions of one link only the one that spent it goes on.
/// </summary>
public class PasswordResetLinkSpendSqlTests
{
    /// <summary>
    /// The statement changes the row only while the link is unused, and the repository reports
    /// whether this call changed it.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the row count not read (true whatever it says).</remarks>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task MarkAsUsed_SpendsTheLinkOnlyWhileUnused_AndSaysWhetherThisCallDid(int rows, bool spent)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: rows);
        var tokenId = Guid.NewGuid();

        var result = await new PasswordResetTokenRepository(db).MarkAsUsedAsync(tokenId, CancellationToken.None);

        result.Should().Be(spent, "only the submission whose statement changed the row spent the link");
        var command = db.Commands.Single();
        Regex.Replace(command.CommandText, @"\s+", " ").Should().Contain(
            "WHERE [Id] = @TokenId AND [UsedAt] IS NULL", "a link already spent is never spent again");
        command.Parameters["TokenId"].Should().Be(tokenId);
    }
}
