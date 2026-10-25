using System.Text.RegularExpressions;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-225 (1) and (3), pinned in CI on the statements the repository sends: an
/// organization application role is written by a serializable update-or-insert on its
/// unique key (a plain INSERT failed on an expired row with 2627), removed by its full
/// key whatever its state with the row count as the answer. The user page's
/// cross-organization read carrying the token read's organization predicates is pinned,
/// on the statements both repositories send, by OrganizationMembershipGrantSqlTests
/// (OI-231). A throwaway LocalDB run of the same repository is in the PR.
/// </summary>
/// <remarks>
/// Deliberate breaks: a plain INSERT; ignore the row count.
/// </remarks>
public class OrganizationAppRoleSqlTests
{
    private static OrganizationUserRole Assignment() =>
        OrganizationUserRole.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

    [Fact]
    public async Task Assign_UpdatesOnTheUniqueKeyUnderASerializableLock_ThenInserts_AndAnswersTheRowItHolds()
    {
        var storedRowId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, scalarFor: _ => storedRowId);

        var answered = await new OrganizationRepository(db).AssignAppRoleAsync(Assignment(), CancellationToken.None);

        answered.Should().Be(storedRowId);
        var sql = Regex.Replace(db.Commands.Should().ContainSingle().Subject.CommandText, @"\s+", " ");
        var steps = new[]
        {
            "SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;",
            "UPDATE [dbo].[OrganizationUserRoles] WITH (UPDLOCK, HOLDLOCK) SET @RowId = [Id], [IsActive] = @IsActive,",
            "WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId AND [ApplicationId] = @ApplicationId AND [RoleId] = @RoleId;",
            "IF @@ROWCOUNT = 0 BEGIN INSERT INTO [dbo].[OrganizationUserRoles]",
            // The outer END closes the membership check (OI-231, OrganizationMembershipGrantSqlTests).
            "SET @RowId = @Id; END; END; COMMIT TRANSACTION; SELECT @RowId;",
        };
        var at = 0;
        foreach (var step in steps)
        {
            var found = sql.IndexOf(step, at, StringComparison.Ordinal);
            found.Should().BeGreaterThanOrEqualTo(0, $"the statement must contain, in order: {step}");
            at = found + step.Length;
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task Remove_DeletesByTheFullKeyWhateverTheState_AndAnswersTheRowCount(int affected, bool removed)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: affected);

        var answered = await new OrganizationRepository(db).RemoveAppRoleAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        answered.Should().Be(removed);
        var sql = db.Commands.Should().ContainSingle().Subject.CommandText;
        sql.Should().Contain("DELETE FROM [dbo].[OrganizationUserRoles]")
            .And.NotContain("[IsActive]", "an inactive row the key would block must be removable too")
            .And.NotContain("[ExpiresAt]", "an expired row the member list shows must be removable");
    }
}
