using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The scoped removal's DELETE as Dapper sends it, for both a role assignment and a
/// direct grant. The bug sat in this SQL condition: the DELETE named the platform
/// scope whatever the request held. <see cref="AssignmentRemovalSqlTests"/> reads the
/// source text, so an inverted <c>if (applicationId.HasValue)</c> keeps both strings
/// present and stays green, and the handler tests mock the repository. Inverted, the
/// DELETE would remove the platform row for an application-scoped removal and nothing
/// for a platform one. These run the method and read the command it issued.
/// </summary>
/// <remarks>
/// Deliberate breaks: invert the <c>if</c> in <c>DeleteUserRoleAsync</c> or
/// <c>DeleteUserPermissionAsync</c>; <c>return true;</c>; <c>return removed == 1;</c>.
/// </remarks>
public class ScopedAssignmentRemovalSqlTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    private static UserRepository Repository(RecordingDbConnectionFactory db) => new(
        db,
        TestHelpers.CreateOptions(new PasswordSettings()),
        Mock.Of<IIdentifierHasher>(),
        TestHelpers.CreateOptions(new AccountDeletionSettings()),
        Mock.Of<IPerUserCryptoService>(),
        Mock.Of<IOtpHasher>());

    /// <summary>
    /// Through the locked change, the one way left to remove either (OI-224): the
    /// lock and the count come first, so the DELETE is the third statement.
    /// </summary>
    private static async Task<bool> Remove(UserRepository repository, string table, Guid? applicationId)
    {
        await using var change = await repository.BeginAuthorityChangeAsync(CancellationToken.None);
        return table == "UserRoles"
            ? await change.RemoveRoleAsync(UserId, ItemId, applicationId, CancellationToken.None)
            : await change.RevokePermissionAsync(UserId, ItemId, applicationId, CancellationToken.None);
    }

    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText, @"\s+", " ").Trim();

    [Theory]
    [InlineData("UserRoles", "RoleId")]
    [InlineData("UserPermissions", "PermissionId")]
    public async Task AnApplicationScopedRemoval_DeletesTheRowOfThatApplication(string table, string idColumn)
    {
        var applicationId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await Remove(Repository(db), table, applicationId);

        db.Commands.Should().HaveCount(3);
        var command = db.Commands[2];
        Sql(command).Should().Be(
            $"DELETE FROM [dbo].[{table}] WHERE [UserId] = @UserId AND [{idColumn}] = @{idColumn} AND [ApplicationId] = @ApplicationId");
        command.Parameters["UserId"].Should().Be(UserId);
        command.Parameters[idColumn].Should().Be(ItemId);
        command.Parameters["ApplicationId"].Should().Be(applicationId);
    }

    [Theory]
    [InlineData("UserRoles", "RoleId")]
    [InlineData("UserPermissions", "PermissionId")]
    public async Task APlatformRemoval_DeletesOnlyThePlatformRow(string table, string idColumn)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await Remove(Repository(db), table, applicationId: null);

        db.Commands.Should().HaveCount(3);
        var command = db.Commands[2];
        Sql(command).Should().Be(
            $"DELETE FROM [dbo].[{table}] WHERE [UserId] = @UserId AND [{idColumn}] = @{idColumn} AND [ApplicationId] IS NULL",
            "SQL reaches a NULL scope only with IS NULL; = @ApplicationId with a null parameter matches no row");
    }

    /// <remarks>
    /// Two rows can go only on a database missing its unique key; that removal still
    /// happened, so it is still reported and audited.
    /// </remarks>
    [Theory]
    [InlineData("UserRoles", 0, false)]
    [InlineData("UserRoles", 1, true)]
    [InlineData("UserRoles", 2, true)]
    [InlineData("UserPermissions", 0, false)]
    [InlineData("UserPermissions", 1, true)]
    [InlineData("UserPermissions", 2, true)]
    public async Task TheAnswer_IsWhetherTheDeleteRemovedARow(string table, int affected, bool expected)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: affected);

        var removed = await Remove(Repository(db), table, Guid.NewGuid());

        removed.Should().Be(expected);
    }
}
