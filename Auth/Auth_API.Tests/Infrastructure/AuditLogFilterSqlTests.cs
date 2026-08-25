using System.Text.RegularExpressions;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the SQL behind "an audit row names two people, and a filter narrows".
/// </summary>
/// <remarks>
/// <para>
/// The handler tests can only prove a value was HANDED OVER to the repository.
/// That is exactly how ActionType and IsSuccess stayed green for the whole time
/// the columns did not exist: the parameters arrived, were bound, and were never
/// referenced by a WHERE clause. The SQL text is the thing under test here, the
/// same approach LoginAttemptPersistenceSqlTests takes — and this file is the one
/// AuditLogHandlerTests points at when it declines to assert the effect itself.
/// </para>
/// <para>
/// The sort assertions exist for a defect of the same shape. "actor" was mapped
/// to the SUBJECT's Users row, so a page ordered by actor was ordered by the
/// people acted upon: an administrator asking what one operator had done got a
/// list keyed to everyone but them, in a screen whose reason for existing is
/// answering that question.
/// </para>
/// </remarks>
public class AuditLogFilterSqlTests
{
    [Fact]
    public void EveryReadJoinsBothPeople()
    {
        var repository = ReadAuditLogRepository();

        // The subject and the performer are two different Users rows, and they
        // coincide only when someone acted on their own account. One join can
        // only ever name one of them.
        repository.Should().Contain("LEFT JOIN [dbo].[Users] u ON a.[UserId] = u.[Id]");
        repository.Should().Contain("LEFT JOIN [dbo].[Users] pb ON a.[PerformedBy] = pb.[Id]");

        // LEFT, not INNER: a system action has neither, and an inner join would
        // drop the retention sweep and the policy publications off every page.
        repository.Should().NotContain("INNER JOIN [dbo].[Users]");

        // Both reading queries take the same joins from one place, so a third
        // one cannot be added with only half of them.
        Regex.Matches(repository, @"FROM \[dbo\]\.\[AuditLogs\] a\{Joins\}")
            .Count.Should().Be(2, "the paged read and the by-entity read both use the shared joins");
    }

    [Fact]
    public void ActorSortsOnThePerformerAndSubjectOnTheSubject()
    {
        var repository = ReadAuditLogRepository();

        repository.Should().Contain(
            $"(SortFields.AuditLogs.Actor, [\"COALESCE(pb.[Email], pb.[FullName])\"])",
            "sorting by actor must order on who performed the action");
        repository.Should().Contain(
            $"(SortFields.AuditLogs.Subject, [\"COALESCE(u.[Email], u.[FullName])\"])",
            "sorting by subject must order on who it happened to");
    }

    [Fact]
    public void TheAllowedSortFields_AreAllMapped()
    {
        var repository = ReadAuditLogRepository();

        // A field the API accepts but the repository does not map falls back to
        // the default order, so the request is honoured in appearance only.
        var unmapped = SortFields.AuditLogs.Allowed
            .Where(field => !repository.Contains(
                $"SortFields.AuditLogs.{char.ToUpperInvariant(field[0])}{field[1..]},",
                StringComparison.Ordinal))
            .ToList();

        unmapped.Should().BeEmpty("every allow-listed sort field needs a column to sort on");
    }

    [Theory]
    [InlineData("ActionType", "a.[ActionType] = @ActionType")]
    [InlineData("IsSuccess", "a.[IsSuccess] = @IsSuccess")]
    [InlineData("Action", "a.[Action]")]
    public void EveryDeclaredFilter_ReachesTheWhereClause(string parameter, string predicate)
    {
        var repository = ReadAuditLogRepository();

        repository.Should().Contain(predicate,
            $"{parameter} is accepted by the query, so it has to narrow the rows");
        repository.Should().Contain($"parameters.Add(\"{parameter}\"",
            $"{parameter} has to be bound, not interpolated");
    }

    [Fact]
    public void TheOutcomeFilter_MatchesOnEquality()
    {
        var repository = ReadAuditLogRepository();

        // Equality, so the rows written before the column existed — IsSuccess
        // NULL, outcome never recorded — fall out of BOTH answers. Anything
        // looser would fold "we do not know" into "it succeeded", which is the
        // defect the nullable column was introduced to end.
        repository.Should().NotContain("ISNULL(a.[IsSuccess]");
        repository.Should().NotContain("COALESCE(a.[IsSuccess]");
    }

    private static string ReadAuditLogRepository() =>
        File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", "AuditLogRepository.cs"));

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
