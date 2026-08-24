using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Holds the audit trail to what it claims.
///
/// Four of the fields the API publishes were not stored at all. The read path
/// filled them in on the way out — ActionType as the literal "System", IsSuccess
/// as true, ErrorMessage and CorrelationId as null — so the audit screen showed
/// every event as a success because it had been told to. PerformedBy was written
/// as the subject's own id, which made "who did this to whom" unanswerable by
/// construction, and SessionId had a column and an index and was never written.
///
/// These tests read the SQL the repository actually issues, so they fail if a
/// field goes back to being invented, dropped, or forced to equal another one.
/// </summary>
public class AuditLogTruthTests
{
    [Fact]
    public async Task CreateAsync_WritesTheActorSeparatelyFromTheSubject()
    {
        var subject = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var session = Guid.NewGuid();

        var command = await CaptureWrite(AuditLog.CreateSuccess(
            actionType: "Authorization",
            action: "permission.granted",
            userId: subject,
            performedBy: actor,
            sessionId: session));

        command.Parameters["UserId"].Should().Be(subject);
        command.Parameters["PerformedBy"].Should().Be(actor,
            "PerformedBy was hardcoded to the subject's id, so an administrator acting on "
            + "someone else's account left a row that named only the account");
        command.Parameters["SessionId"].Should().Be(session,
            "the column and IX_AuditLogs_SessionId have always existed and nothing filled them");
    }

    [Fact]
    public async Task CreateAsync_WritesTheOutcomeAndItsReason()
    {
        var command = await CaptureWrite(AuditLog.CreateFailure(
            actionType: "Authorization",
            action: "permission.granted",
            errorMessage: "actor does not hold the permission",
            userId: Guid.NewGuid(),
            performedBy: Guid.NewGuid()));

        command.Parameters["IsSuccess"].Should().Be(false,
            "a failure that is stored as a success is worse than one that is not stored");
        command.Parameters["ErrorMessage"].Should().Be("actor does not hold the permission");
        command.Parameters["ActionType"].Should().Be("Authorization");
    }

    [Fact]
    public async Task CreateAsync_WritesBothSidesOfAChange()
    {
        var command = await CaptureWrite(AuditLog.CreateSuccess(
            actionType: "UserManagement",
            action: "user.updated",
            userId: Guid.NewGuid(),
            performedBy: Guid.NewGuid(),
            oldValues: "{\"email\":\"before@example.com\"}",
            newValues: "{\"email\":\"after@example.com\"}"));

        // The columns predate all of this; what was missing was anyone passing
        // them. "What changed" is not answerable from one side alone.
        command.Parameters["OldValues"].Should().Be("{\"email\":\"before@example.com\"}");
        command.Parameters["NewValues"].Should().Be("{\"email\":\"after@example.com\"}");
    }

    [Theory]
    [InlineData("ActionType")]
    [InlineData("IsSuccess")]
    [InlineData("ErrorMessage")]
    [InlineData("CorrelationId")]
    [InlineData("PerformedBy")]
    [InlineData("SessionId")]
    public async Task CreateAsync_NamesEveryColumnThatUsedToBeInvented(string column)
    {
        var command = await CaptureWrite(AuditLog.CreateSuccess(
            actionType: "System", action: "system.started"));

        command.CommandText.Should().Contain($"[{column}]",
            $"{column} is published to API consumers, so a row that does not carry it is a row "
            + "that will be answered for from somewhere other than the database");
    }

    [Fact]
    public async Task GetPaged_AppliesTheOutcomeAndCategoryFilters()
    {
        var command = await CaptureRead(actionType: "Authorization", isSuccess: false);

        command.CommandText.Should().Contain("[ActionType] = @ActionType");
        command.CommandText.Should().Contain("[IsSuccess] = @IsSuccess");
        command.Parameters["ActionType"].Should().Be("Authorization");
        command.Parameters["IsSuccess"].Should().Be(false);
    }

    [Fact]
    public async Task GetPaged_LeavesTheFiltersOutWhenNoneWereAskedFor()
    {
        // Equality, not "is not the other one": rows written before IsSuccess
        // existed are NULL, and an unfiltered page must still return them.
        var command = await CaptureRead(actionType: null, isSuccess: null);

        command.CommandText.Should().NotContain("[IsSuccess]");
        command.CommandText.Should().NotContain("[ActionType] =");
    }

    private static async Task<RecordedCommand> CaptureWrite(AuditLog log)
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 1);
        await new AuditLogRepository(factory).CreateAsync(log, CancellationToken.None);

        factory.LastCommand.Should().NotBeNull("the repository must have issued a command");
        return factory.LastCommand!;
    }

    private static async Task<RecordedCommand> CaptureRead(string? actionType, bool? isSuccess)
    {
        var factory = new RecordingDbConnectionFactory(affectedRows: 0);

        await new AuditLogRepository(factory).GetPagedAsync(
            pageNumber: 1,
            pageSize: 50,
            userId: null,
            applicationId: null,
            action: null,
            actionType: actionType,
            isSuccess: isSuccess,
            fromDate: null,
            toDate: null,
            sortBy: null,
            sortDirection: SortDirection.Desc,
            cancellationToken: CancellationToken.None);

        factory.LastCommand.Should().NotBeNull("the repository must have issued a command");
        return factory.LastCommand!;
    }
}
