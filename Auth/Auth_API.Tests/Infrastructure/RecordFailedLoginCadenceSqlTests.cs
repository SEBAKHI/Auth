using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// X12, LK2: one wrong password is one statement, with the limits as parameters,
/// and the transition it reports is read from that statement's OUTPUT. In CI the
/// statement's text and parameters are the unit (no database); the cadence on SQL
/// Server — Max′ per window, the count kept across lapses, the hard lock at Hard′
/// — is proven by the probe Tools/probes/account-lock.mjs (owner step on dev).
/// </summary>
/// <remarks>
/// Deliberate break: pass the hard limit only under Legacy (or never). The
/// parameter assertions below turn red.
/// </remarks>
public class RecordFailedLoginCadenceSqlTests
{
    [Fact]
    public async Task RecordFailedLogin_IsOneStatement_WithTheLimitsAsParameters()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => null);

        await LockSql.Repository(db).RecordFailedLoginAsync(userId, 100, 5, TimeSpan.FromMinutes(15), CancellationToken.None);

        db.Commands.Should().ContainSingle("the count and the lock move together, or a burst races between them");
        var command = db.LastCommand!;
        LockSql.Normalize(command.CommandText).Should().Be(LockSql.Normalize(Auth.Infrastructure.Persistence.UserRepository.RecordFailedLoginSql));
        command.Parameters["UserId"].Should().Be(userId);
        command.Parameters["Hard"].Should().Be(100);
        command.Parameters["Max"].Should().Be(5);
        command.Parameters["LockoutMinutes"].Should().Be(15);
    }

    [Fact]
    public async Task RecordFailedLogin_WithNoHardLimit_SendsNull()
    {
        // Legacy (LK9.2 (ii)): no new hard lock, through the same statement.
        var db = new RecordingDbConnectionFactory(affectedRows: 1, rowFor: _ => null);

        await LockSql.Repository(db).RecordFailedLoginAsync(Guid.NewGuid(), null, 5, TimeSpan.FromMinutes(15), CancellationToken.None);

        db.LastCommand!.Parameters["Hard"].Should().BeOneOf(null, DBNull.Value);
    }

    [Fact]
    public async Task RecordFailedLogin_ReportsTheHardLockItWrote()
    {
        var userId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 1,
            rowFor: _ => LockSql.Row(1, 99, null, (int)UserStatus.Locked, 100, null));

        var transition = await LockSql.Repository(db).RecordFailedLoginAsync(
            userId, 100, 5, TimeSpan.FromMinutes(15), CancellationToken.None);

        transition.Should().NotBeNull();
        transition!.UserId.Should().Be(userId);
        transition.After.FailedAttempts.Should().Be(100);
        transition.StartedHardLock(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public async Task RecordFailedLogin_WhenNoRowMatched_ReportsNothing()
    {
        // A live administrator's lock, a deleted or inactive row: nothing counted.
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => null);

        var transition = await LockSql.Repository(db).RecordFailedLoginAsync(
            Guid.NewGuid(), 100, 5, TimeSpan.FromMinutes(15), CancellationToken.None);

        transition.Should().BeNull();
    }

    [Fact]
    public async Task RecordFailedLogin_RefusesAThresholdBelowOne()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var act = () => LockSql.Repository(db).RecordFailedLoginAsync(Guid.NewGuid(), 100, 0, TimeSpan.FromMinutes(15), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>("a modulo by zero would fail on the server instead");
        db.Commands.Should().BeEmpty();
    }
}
