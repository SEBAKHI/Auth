using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.ApiKeyManagement;

/// <summary>
/// The API key repository's writes and its one-row lookup (OI-128 PR B), read off the commands it
/// sends. Creation and rotation are one transaction each: a failing scope insert commits nothing,
/// so no key without its scopes is ever left behind.
/// </summary>
public class ApiKeyRepositorySqlTests
{
    private static ApiKey Key() => ApiKey.Create(
        Guid.NewGuid(), "Key", "ak_prod_", "Sel3ctor0000000A", "hash", Guid.NewGuid(),
        allowedIps: IpAllowList.Create(["203.0.113.0/24"]).Value);

    [Fact]
    public async Task CreateAsync_WritesTheKeyAndItsScopes_InOneCommittedTransaction()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var key = Key();

        await new ApiKeyRepository(db).CreateAsync(
            key, [ApiKeyScope.Create(key.Id, Guid.NewGuid(), Guid.NewGuid()), ApiKeyScope.Create(key.Id, Guid.NewGuid(), Guid.NewGuid())],
            CancellationToken.None);

        db.Commands.Should().HaveCount(3).And.OnlyContain(command => command.InTransaction);
        db.Commands[0].CommandText.Should().Contain("INSERT INTO [dbo].[ApiKeys]").And.Contain("[KeySelector]")
            .And.NotContain("[RateLimitPerMinute]").And.NotContain("[AllowedOrigins]");
        db.Commands[0].Parameters["KeySelector"].Should().Be("Sel3ctor0000000A");
        db.Commands[0].Parameters["AllowedIps"].Should().Be("[\"203.0.113.0/24\"]");
        db.Commands.Skip(1).Should().OnlyContain(command => command.CommandText.Contains("INSERT INTO [dbo].[ApiKeyScopes]"));
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WhenAScopeInsertFails_CommitsNothing()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            throwOn: command => command.CommandText.Contains("[ApiKeyScopes]") ? new InvalidOperationException("FK violation") : null);
        var key = Key();

        var create = () => new ApiKeyRepository(db).CreateAsync(
            key, [ApiKeyScope.Create(key.Id, Guid.NewGuid(), Guid.NewGuid())], CancellationToken.None);

        await create.Should().ThrowAsync<InvalidOperationException>();
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeFalse("the key row goes with the failed scope");
    }

    [Fact]
    public async Task TryRotateAsync_MovesTheOldKeysEndThenWritesTheNewKeyAndItsScopes_InOneCommittedTransaction()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var replaced = TestHelpers.CreateApiKey();
        replaced.ScheduleExpiration(DateTime.UtcNow.AddHours(1));
        var replacement = Key();

        var rotated = await new ApiKeyRepository(db).TryRotateAsync(
            replaced, replacement, [ApiKeyScope.Create(replacement.Id, Guid.NewGuid(), Guid.NewGuid())], CancellationToken.None);

        rotated.Should().BeTrue();
        db.Commands.Should().HaveCount(3).And.OnlyContain(command => command.InTransaction);
        db.Commands[0].CommandText.Should().Contain("UPDATE [dbo].[ApiKeys] SET [ExpiresAt] = @ExpiresAt")
            .And.Contain("[RevokedAt] IS NULL");
        db.Commands[0].Parameters["Id"].Should().Be(replaced.Id);
        db.Commands[1].CommandText.Should().Contain("INSERT INTO [dbo].[ApiKeys]");
        db.Commands[2].CommandText.Should().Contain("INSERT INTO [dbo].[ApiKeyScopes]");
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task TryRotateAsync_WhenTheKeyWasRevokedMeanwhile_WritesNothing()
    {
        // The guarded update matches no row: a revocation landed between the read and the write.
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var replaced = TestHelpers.CreateApiKey();
        replaced.ScheduleExpiration(DateTime.UtcNow.AddHours(1));
        var replacement = Key();

        var rotated = await new ApiKeyRepository(db).TryRotateAsync(
            replaced, replacement, [ApiKeyScope.Create(replacement.Id, Guid.NewGuid(), Guid.NewGuid())], CancellationToken.None);

        rotated.Should().BeFalse();
        db.Commands.Should().ContainSingle().Which.CommandText.Should().Contain("UPDATE [dbo].[ApiKeys]");
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveBySelectorAsync_FindsOneLiveKey_ByItsSelector()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new ApiKeyRepository(db).GetActiveBySelectorAsync("Sel3ctor0000000A", CancellationToken.None);

        var sql = db.LastCommand!.CommandText;
        sql.Should().Contain("WHERE k.[KeySelector] = @Selector")
            .And.Contain("k.[RevokedAt] IS NULL")
            .And.Contain("k.[ExpiresAt] IS NULL OR k.[ExpiresAt] > GETUTCDATE()")
            .And.Contain("a.[IsActive] = 1 AND a.[IsDeleted] = 0");
        db.LastCommand.Parameters["Selector"].Should().Be("Sel3ctor0000000A");
    }
}
