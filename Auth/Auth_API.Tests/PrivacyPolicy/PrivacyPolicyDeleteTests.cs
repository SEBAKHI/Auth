using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.DeletePrivacyPolicyVersion;
using Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyVersions;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Persistence;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.PrivacyPolicy;

/// <summary>
/// Deleting a privacy-policy revision (D1 of the UI batch): only a draft nobody was shown or told
/// about goes. Published, superseded (served documents), announced, in-use and cited revisions are
/// legal evidence and are refused with <c>PrivacyPolicy.NotDeletable</c>, by the entity, the
/// handler, and again by the delete statement.
/// </summary>
/// <remarks>
/// Deliberate breaks: drop any one condition from <c>EnsureDeletable</c>; drop the citation check
/// from the handler; drop any condition, or the HOLDLOCK, from the delete statement. The race of the
/// statement against a concurrent publish was run against a throwaway LocalDB database (see the PR).
/// </remarks>
public class PrivacyPolicyDeleteTests
{
    private const string InUse = "2026.07";
    private static readonly Guid Admin = Guid.NewGuid();

    private static PrivacyPolicyVersion Revision(
        string version = "2026.11", bool published = false, DateTime? notifiedAt = null, DateTime? firstPublishedAt = null) =>
        new(Guid.NewGuid(), version, DateTime.UtcNow, published, null, notifiedAt,
            notifiedAt is null ? null : 3, DateTime.UtcNow, Admin, firstPublishedAt ?? (published ? DateTime.UtcNow : null));

    [Fact]
    public void EnsureDeletable_AllowsOnlyADraftNobodyWasShownOrTold()
    {
        Revision().EnsureDeletable(hasServedDocuments: false, InUse).IsError.Should().BeFalse();

        Revision(published: true).EnsureDeletable(false, InUse).FirstError.Code.Should().Be("PrivacyPolicy.NotDeletable");
        Revision(notifiedAt: DateTime.UtcNow).EnsureDeletable(false, InUse).FirstError.Code.Should().Be("PrivacyPolicy.NotDeletable");
        Revision().EnsureDeletable(hasServedDocuments: true, InUse).FirstError.Code.Should().Be("PrivacyPolicy.NotDeletable",
            "a superseded revision is back to IsPublished = 0, but its served documents say it was published once");
        Revision(InUse).EnsureDeletable(false, InUse).FirstError.Code.Should().Be("PrivacyPolicy.NotDeletable",
            "account deletions are stamped with the configured version");
        Revision(firstPublishedAt: DateTime.UtcNow).EnsureDeletable(false, InUse).FirstError.Code.Should().Be(
            "PrivacyPolicy.NotDeletable",
            "the seeded revision is published without served documents; superseded, only this mark says so");
    }

    private sealed class World
    {
        public Mock<IPrivacyPolicyVersionRepository> Versions { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public PrivacyPolicyVersion Revision { get; }

        public World(PrivacyPolicyVersion revision, bool served = false, bool cited = false, bool deleted = true)
        {
            Revision = revision;
            Versions.Setup(r => r.GetByIdAsync(revision.Id, It.IsAny<CancellationToken>())).ReturnsAsync(revision);
            Versions.Setup(r => r.GetServedVersionIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(served ? new HashSet<Guid> { revision.Id } : new HashSet<Guid>());
            Versions.Setup(r => r.IsCitedByDeletionRecordsAsync(revision.Version, It.IsAny<CancellationToken>())).ReturnsAsync(cited);
            Versions.Setup(r => r.TryDeleteDraftAsync(revision.Id, InUse, It.IsAny<CancellationToken>())).ReturnsAsync(deleted);
        }

        public Task<ErrorOr.ErrorOr<ErrorOr.Deleted>> Delete() =>
            new DeletePrivacyPolicyVersionCommandHandler(
                    Versions.Object,
                    TestHelpers.CreateOptions(new AccountDeletionSettings { PolicyVersion = InUse }),
                    Publisher.Object,
                    TimeProvider.System,
                    new Mock<ILogger<DeletePrivacyPolicyVersionCommandHandler>>().Object)
                .Handle(new DeletePrivacyPolicyVersionCommand(Revision.Id) { RequestedBy = Admin }, CancellationToken.None);

        public void NothingDeleted()
        {
            Versions.Verify(r => r.TryDeleteDraftAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task Delete_Draft_DeletesAndPublishes()
    {
        var world = new World(Revision());

        var result = await world.Delete();

        result.IsError.Should().BeFalse();
        world.Publisher.Verify(p => p.Publish(
            It.Is<PrivacyPolicyVersionDeletedEvent>(e =>
                e.PrivacyPolicyVersionId == world.Revision.Id && e.Version == "2026.11" &&
                e.TriggeredBy == Admin && e.EventId != Guid.Empty),
            CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData("published")]
    [InlineData("superseded")]
    [InlineData("published-once")]
    [InlineData("notified")]
    [InlineData("in-use")]
    [InlineData("cited")]
    public async Task Delete_Evidence_IsRefusedWith409_AndNothingIsDeleted(string kind)
    {
        var world = kind switch
        {
            "published" => new World(Revision(published: true)),
            "superseded" => new World(Revision(), served: true),
            "published-once" => new World(Revision(firstPublishedAt: DateTime.UtcNow)),
            "notified" => new World(Revision(notifiedAt: DateTime.UtcNow)),
            "in-use" => new World(Revision(InUse)),
            _ => new World(Revision(), cited: true),
        };

        var result = await world.Delete();

        result.FirstError.Code.Should().Be("PrivacyPolicy.NotDeletable");
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
        world.NothingDeleted();
    }

    [Fact]
    public async Task Delete_PublishedMeanwhile_IsRefused_DeletedMeanwhile_IsNotFound()
    {
        var raced = new World(Revision(), deleted: false);
        (await raced.Delete()).FirstError.Code.Should().Be("PrivacyPolicy.NotDeletable");
        raced.Publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);

        var gone = new World(Revision(), deleted: false);
        gone.Versions.SetupSequence(r => r.GetByIdAsync(gone.Revision.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gone.Revision)
            .ReturnsAsync((PrivacyPolicyVersion?)null);
        (await gone.Delete()).FirstError.Code.Should().Be("PrivacyPolicy.NotFound");

        var missing = new World(Revision());
        missing.Versions.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PrivacyPolicyVersion?)null);
        (await missing.Delete()).FirstError.Code.Should().Be("PrivacyPolicy.NotFound");
        missing.NothingDeleted();
    }

    /// <summary>
    /// The statement repeats every condition, so a revision published, announced or cited between
    /// the handler's read and the delete stays; HOLDLOCK keeps a publish from inserting served
    /// documents between the check and the delete.
    /// </summary>
    [Fact]
    public async Task Repository_DeleteStatement_RepeatsEveryCondition()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var id = Guid.NewGuid();

        var deleted = await new PrivacyPolicyVersionRepository(db).TryDeleteDraftAsync(id, InUse, CancellationToken.None);

        deleted.Should().BeTrue();
        Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ").Trim().Should().Be(
            "DELETE v FROM [dbo].[PrivacyPolicyVersions] v WHERE v.[Id] = @Id AND v.[IsPublished] = 0 " +
            "AND v.[FirstPublishedAtUtc] IS NULL " +
            "AND v.[NotifiedAtUtc] IS NULL AND v.[Version] <> @PolicyVersionInUse " +
            "AND NOT EXISTS ( SELECT 1 FROM [dbo].[PrivacyPolicyArtifacts] a WITH (UPDLOCK, HOLDLOCK) WHERE a.[VersionId] = v.[Id]) " +
            "AND NOT ( (EXISTS (SELECT 1 FROM [dbo].[AccountDeletionRequests] r WHERE r.[PolicyVersion] = v.[Version]) " +
            "OR EXISTS (SELECT 1 FROM [dbo].[AccountDeletionTombstones] t WHERE t.[PolicyVersion] = v.[Version])))");
        db.LastCommand.Parameters["Id"].Should().Be(id);
        db.LastCommand.Parameters["PolicyVersionInUse"].Should().Be(InUse);
    }

    /// <summary>
    /// Publishing stamps the first publication once and never clears it, so the revision it
    /// supersedes keeps its own stamp and stays undeletable.
    /// </summary>
    [Fact]
    public async Task Repository_Publish_StampsFirstPublication_AndNeverClearsIt()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new PrivacyPolicyVersionRepository(db).PublishArtifactsAsync(Guid.NewGuid(), [], CancellationToken.None);

        var update = db.Commands.Select(c => Regex.Replace(c.CommandText, @"\s+", " ").Trim())
            .Single(text => text.StartsWith("UPDATE [dbo].[PrivacyPolicyVersions]"));
        update.Should().Contain(
            "[FirstPublishedAtUtc] = CASE WHEN [Id] = @VersionId THEN COALESCE([FirstPublishedAtUtc], SYSUTCDATETIME()) ELSE [FirstPublishedAtUtc] END");
    }

    [Fact]
    public async Task VersionsList_OffersDelete_OnlyForADraftNobodyWasShownOrTold()
    {
        var draft = Revision("2026.11");
        var superseded = Revision("2026.08");
        var published = Revision("2026.09", published: true);
        var notified = Revision("2026.10", notifiedAt: DateTime.UtcNow);
        var inUse = Revision(InUse);
        var seeded = Revision("2026.06", firstPublishedAt: new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc));
        var repository = new Mock<IPrivacyPolicyVersionRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([draft, superseded, published, notified, inUse, seeded]);
        repository.Setup(r => r.GetServedVersionIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { superseded.Id, published.Id });
        repository.Setup(r => r.GetTranslationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await new GetPrivacyPolicyVersionsQueryHandler(
                repository.Object,
                TestHelpers.CreateOptions(new AccountDeletionSettings { PolicyVersion = InUse }),
                TestHelpers.CreateOptions(new DataControllerSettings()))
            .Handle(new GetPrivacyPolicyVersionsQuery(), CancellationToken.None);

        result.Value.ToDictionary(v => v.Version, v => v.CanDelete).Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["2026.11"] = true,
            ["2026.08"] = false,
            ["2026.09"] = false,
            ["2026.10"] = false,
            [InUse] = false,
            ["2026.06"] = false,
        });
    }

    [Fact]
    public async Task AuditHandler_WritesTheDeletion_AndNeverThrows()
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var handler = new PrivacyPolicyVersionDeletedAuditEventHandler(
            audit.Object, new Mock<ILogger<PrivacyPolicyVersionDeletedAuditEventHandler>>().Object);
        var versionId = Guid.NewGuid();

        await handler.Handle(
            new PrivacyPolicyVersionDeletedEvent(Guid.NewGuid(), versionId, "2026.11", Admin, DateTimeOffset.UtcNow),
            CancellationToken.None);

        written!.Action.Should().Be(AuditActions.PrivacyPolicyVersionDeleted);
        written.EntityId.Should().Be(versionId);
        written.PerformedBy.Should().Be(Admin);
        written.OldValues.Should().Contain("2026.11");

        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var act = () => handler.Handle(
            new PrivacyPolicyVersionDeletedEvent(Guid.NewGuid(), versionId, "2026.11", Admin, DateTimeOffset.UtcNow),
            CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
