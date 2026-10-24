using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using MediatR;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-225 (16): a grant at an application's scope writes access only when it is
/// missing. When an invitation (or another grant) inserts the access row between the
/// grant's read and its insert, the unique (application, user) constraint refuses the
/// grant's insert; the row that won keeps its expiry and note, and the grant records
/// no access event. Two invitations keep last-writer-wins.
/// </summary>
public class ApplicationAccessGrantRaceTests
{
    private const string Insert = "INSERT INTO [dbo].[ApplicationUserAccess]";
    private const string Reinstate = "UPDATE [dbo].[ApplicationUserAccess] SET";

    private static RecordingDbConnectionFactory InsertLosesTheRace() => new(
        affectedRows: 1,
        throwOn: command => command.CommandText.Contains(Insert) ? SqlExceptions.WithNumber(2627) : null);

    private static ApplicationUserAccess Grant() =>
        ApplicationUserAccess.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(30), note: null);

    /// <remarks>Deliberate break: reinstate the winner's row with this call's terms on a collision.</remarks>
    [Fact]
    public async Task CreateGrantIfAbsent_WhenTheInsertCollides_LeavesTheWinnersRow_AndReturnsFalse()
    {
        var db = InsertLosesTheRace();

        var written = await new ApplicationAccessRepository(db).CreateGrantIfAbsentAsync(Grant(), CancellationToken.None);

        written.Should().BeFalse();
        db.Commands.Should().ContainSingle("the refused insert is the only statement: nothing rewrites the winner's row")
            .Which.CommandText.Should().Contain(Insert);
    }

    [Fact]
    public async Task CreateGrantIfAbsent_WhenTheRowIsAbsent_InsertsIt_AndReturnsTrue()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var grant = Grant();

        var written = await new ApplicationAccessRepository(db).CreateGrantIfAbsentAsync(grant, CancellationToken.None);

        written.Should().BeTrue();
        db.Commands.Should().ContainSingle().Which.Parameters["ExpiresAt"].Should().Be(grant.ExpiresAt);
    }

    /// <summary>The invitation path is unchanged: on a collision the last writer's terms stand.</summary>
    [Fact]
    public async Task CreateGrant_WhenTheInsertCollides_StillReinstatesWithThisCallsTerms()
    {
        var db = InsertLosesTheRace();

        await new ApplicationAccessRepository(db).CreateGrantAsync(Grant(), CancellationToken.None);

        db.Commands.Select(c => c.CommandText).Should().SatisfyRespectively(
            insert => insert.Should().Contain(Insert),
            update => update.Should().Contain(Reinstate));
    }

    /// <remarks>Deliberate break: create through WriteAsync (CreateGrantAsync) in EnsureAsync again.</remarks>
    [Fact]
    public async Task Ensure_WhenAConcurrentWriteInsertedTheRow_ReportsNothingWritten()
    {
        var access = new Mock<IApplicationAccessRepository>();
        access.Setup(r => r.CreateGrantIfAbsentAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var publisher = new Mock<IPublisher>();
        var application = TestHelpers.CreateApplication();

        var wrote = await TestHelpers.CreateApplicationAccessWriter(access, publisher)
            .EnsureAsync(application, Guid.NewGuid(), Guid.NewGuid(), expiresAt: null, CancellationToken.None);

        wrote.Should().BeFalse("the caller publishes the access event only for a write it made");
        access.Verify(r => r.CreateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()), Times.Never());
        access.Verify(r => r.UpdateGrantAsync(It.IsAny<ApplicationUserAccess>(), It.IsAny<CancellationToken>()), Times.Never());
        publisher.Invocations.Should().BeEmpty();
    }
}
