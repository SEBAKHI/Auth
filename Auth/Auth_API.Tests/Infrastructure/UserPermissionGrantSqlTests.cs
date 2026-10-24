using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-223 T2: the direct grant's write as Dapper sends it. UQ_UserPermissions is
/// (UserId, PermissionId, ApplicationId) with neither IsActive nor ExpiresAt, so a
/// lapsed or deactivated grant keeps its row, and the plain INSERT this replaced hit
/// the key (2627, a 500) on every re-grant of one. The handler tests mock the
/// repository; these read the statement it issues.
/// </summary>
/// <remarks>
/// What the statement does on SQL Server was run on a throwaway LocalDB database: an
/// expired and a deactivated row re-granted in place (one row, new expiry, active), a
/// fresh grant inserted, and a grant at another scope kept beside it. Deliberate
/// break: the plain INSERT again.
/// </remarks>
public class UserPermissionGrantSqlTests
{
    private static UserRepository Repository(RecordingDbConnectionFactory db) => new(
        db,
        TestHelpers.CreateOptions(new PasswordSettings()),
        Mock.Of<IIdentifierHasher>(),
        TestHelpers.CreateOptions(new AccountDeletionSettings()),
        Mock.Of<IPerUserCryptoService>(),
        Mock.Of<IOtpHasher>());

    private static string Sql(RecordedCommand command) =>
        Regex.Replace(command.CommandText, @"\s+", " ").Trim();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AGrant_UpdatesTheRowOfItsFullKey_AndInsertsOnlyWhenThereIsNone(bool applicationScoped)
    {
        Guid? applicationId = applicationScoped ? Guid.NewGuid() : null;
        var grant = UserPermission.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), applicationId, DateTime.UtcNow.AddDays(7));
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await Repository(db).GrantPermissionAsync(grant, CancellationToken.None);

        db.Commands.Should().ContainSingle("one statement, so the update and the insert share one serializable transaction");
        var sql = Sql(db.Commands[0]);
        sql.Should().Contain("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION;");
        sql.Should().Contain(
            "UPDATE [dbo].[UserPermissions] WITH (UPDLOCK, HOLDLOCK) SET [GrantedAt] = @GrantedAt, [GrantedBy] = @GrantedBy, [ExpiresAt] = @ExpiresAt, [IsActive] = @IsActive "
            + "WHERE [UserId] = @UserId AND [PermissionId] = @PermissionId AND ([ApplicationId] = @ApplicationId OR ([ApplicationId] IS NULL AND @ApplicationId IS NULL));",
            "the row of the same (user, permission, scope) is restored in place, whatever its IsActive or expiry");
        sql.Should().Contain("IF @@ROWCOUNT = 0 BEGIN INSERT INTO [dbo].[UserPermissions]");
        sql.IndexOf("UPDATE [dbo].[UserPermissions]", StringComparison.Ordinal)
            .Should().BeLessThan(sql.IndexOf("INSERT INTO [dbo].[UserPermissions]", StringComparison.Ordinal));

        var parameters = db.Commands[0].Parameters;
        parameters["UserId"].Should().Be(grant.UserId);
        parameters["PermissionId"].Should().Be(grant.PermissionId);
        parameters["ApplicationId"].Should().Be(applicationId);
        parameters["ExpiresAt"].Should().Be(grant.ExpiresAt);
        parameters["IsActive"].Should().Be(true);
    }
}
