using System.Text.RegularExpressions;
using Auth.Domain.Entities;
using Auth.Domain.ReadModels.Organizations;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards OI-63's one write: <c>OrganizationRepository.ProvisionForApplicationAsync</c>,
/// which creates (or takes) the user's organization, enables the application and
/// grants the creator role in ONE transaction.
/// <para>
/// The test project has no database, so two kinds of evidence stand in for one:
/// the method runs end to end on a recording connection (every statement and
/// whether it carried the transaction, and how the transaction ended), and the
/// SQL text is checked for what the recording cannot evaluate: the lock, the
/// filters, and the predicate shared with the authorize endpoint.
/// </para>
/// </summary>
public class OrganizationProvisioningSqlTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ApplicationId = Guid.NewGuid();
    private static readonly Guid CreatorRoleId = Guid.NewGuid();
    private static readonly Guid OwnerRoleId = Guid.NewGuid();

    private static readonly string[] FourTables =
        ["Organizations", "OrganizationUsers", "OrganizationApplications", "OrganizationUserRoles"];

    private static string Source() => File.ReadAllText(Path.Combine(
        ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "OrganizationRepository.cs"));

    private static string ProvisionBody()
    {
        var source = Source();
        var start = source.IndexOf("ProvisionForApplicationAsync(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "ProvisionForApplicationAsync must exist in OrganizationRepository");
        var end = source.IndexOf("#endregion", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static string ConstantSql(string name)
    {
        var match = Regex.Match(Source(), $@"private const string {name} = @""(?<sql>[^""]*)"";");
        match.Success.Should().BeTrue($"{name} is declared once, as a constant");
        return match.Groups["sql"].Value;
    }

    private static OrganizationProvisioningRequest NewOrganizationRequest(int max = 1) =>
        OrganizationProvisioningRequest.ForNewOrganization(
            Organization.Create("org-abcdefghij", "مؤسسة الاختبار", "owner@example.com", UserId),
            ApplicationId, CreatorRoleId, OwnerRoleId, max);

    private static OrganizationProvisioningRequest ExistingOrganizationRequest(Guid organizationId) =>
        OrganizationProvisioningRequest.ForExistingOrganization(organizationId, UserId, ApplicationId, CreatorRoleId);

    /// <summary>
    /// A connection on which the locked count answers <paramref name="owned"/>, the
    /// "already set up" lookup answers <paramref name="alreadySetUp"/>, the
    /// existing-organization lookup <paramref name="eligible"/>, and the closing
    /// postcondition <paramref name="setUpAfterWrites"/>.
    /// </summary>
    private static RecordingDbConnectionFactory Connection(
        int owned = 0,
        Guid? alreadySetUp = null,
        Guid? eligible = null,
        int setUpAfterWrites = 1) =>
        new(
            affectedRows: 1,
            scalarFor: command => command.CommandText switch
            {
                var sql when sql.Contains("WITH (UPDLOCK, HOLDLOCK)") => owned,
                var sql when sql.Contains("SELECT TOP 1 o.[Id]") => alreadySetUp,
                var sql when sql.Contains("WITH (UPDLOCK)") => eligible,
                var sql when sql.Contains("SELECT COUNT(1) FROM [dbo].[Organizations] o WHERE o.[Id] = @OrganizationId") => setUpAfterWrites,
                _ => throw new InvalidOperationException($"Unexpected scalar: {command.CommandText}"),
            });

    private static IEnumerable<string> InsertedTables(RecordingDbConnectionFactory db) =>
        db.Commands
            .Select(command => Regex.Match(command.CommandText, @"INSERT INTO \[dbo\]\.\[(?<table>\w+)\]"))
            .Where(match => match.Success)
            .Select(match => match.Groups["table"].Value);

    [Fact]
    public async Task NewOrganization_WritesTheFourTables_InOneCommittedTransaction()
    {
        var db = Connection();

        var outcome = await new OrganizationRepository(db).ProvisionForApplicationAsync(
            NewOrganizationRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OrganizationProvisioningStatus.Provisioned);
        outcome.OrganizationCreated.Should().BeTrue();
        InsertedTables(db).Should().Equal(FourTables);
        db.Commands.Count.Should().BeGreaterThan(0);
        db.Commands.Should().OnlyContain(command => command.InTransaction,
            "a write on its own connection would commit alone and break all-or-nothing");
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task NewOrganization_IsNeverAutoCreated_AndOwnedByTheUser()
    {
        var db = Connection();

        await new OrganizationRepository(db).ProvisionForApplicationAsync(NewOrganizationRequest(), CancellationToken.None);

        var insert = db.Commands.Single(command => command.CommandText.Contains("INSERT INTO [dbo].[Organizations]"));
        insert.CommandText.Should().MatchRegex(@"@IsActive,\s*0,", "IsAutoCreated is written as 0, never taken from the caller");
        insert.Parameters["OwnerId"].Should().Be(UserId);

        var membership = db.Commands.Single(command => command.CommandText.Contains("INSERT INTO [dbo].[OrganizationUsers]"));
        membership.Parameters["RoleId"].Should().Be(OwnerRoleId);

        var grant = db.Commands.Single(command => command.CommandText.Contains("INSERT INTO [dbo].[OrganizationUserRoles]"));
        grant.Parameters["RoleId"].Should().Be(CreatorRoleId);
        grant.Parameters["ApplicationId"].Should().Be(ApplicationId);
        grant.Parameters["UserId"].Should().Be(UserId);
    }

    [Fact]
    public async Task AtTheLimit_NothingIsWritten_AndTheTransactionRollsBack()
    {
        var db = Connection(owned: 1);

        var outcome = await new OrganizationRepository(db).ProvisionForApplicationAsync(
            NewOrganizationRequest(max: 1), CancellationToken.None);

        outcome.Status.Should().Be(OrganizationProvisioningStatus.LimitReached);
        InsertedTables(db).Should().BeEmpty();
        db.Transactions.Should().ContainSingle().Which.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task LimitZero_RefusesEveryCreation()
    {
        var db = Connection(owned: 0);

        var outcome = await new OrganizationRepository(db).ProvisionForApplicationAsync(
            NewOrganizationRequest(max: 0), CancellationToken.None);

        outcome.Status.Should().Be(OrganizationProvisioningStatus.LimitReached);
        InsertedTables(db).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AlreadySetUp_ChangesNothing_AndReturnsThatOrganization(bool newOrganization)
    {
        var existing = Guid.NewGuid();
        var db = Connection(alreadySetUp: existing, eligible: Guid.NewGuid());

        var outcome = await new OrganizationRepository(db).ProvisionForApplicationAsync(
            newOrganization ? NewOrganizationRequest() : ExistingOrganizationRequest(Guid.NewGuid()),
            CancellationToken.None);

        outcome.Status.Should().Be(OrganizationProvisioningStatus.AlreadySetUp);
        outcome.OrganizationId.Should().Be(existing);
        db.Commands.Should().NotContain(command =>
            command.CommandText.Contains("INSERT INTO") || command.CommandText.Contains("UPDATE [dbo]"));
    }

    [Fact]
    public async Task ExistingOrganization_EnablesAndGrants_WithoutCreatingAnything()
    {
        var organizationId = Guid.NewGuid();
        var db = Connection(eligible: organizationId);

        var outcome = await new OrganizationRepository(db).ProvisionForApplicationAsync(
            ExistingOrganizationRequest(organizationId), CancellationToken.None);

        outcome.Status.Should().Be(OrganizationProvisioningStatus.Provisioned);
        outcome.OrganizationId.Should().Be(organizationId);
        outcome.OrganizationCreated.Should().BeFalse();
        InsertedTables(db).Should().Equal(["OrganizationApplications", "OrganizationUserRoles"]);
        db.Commands.Should().OnlyContain(command => command.InTransaction);
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task ExistingOrganization_OnlyInsertsWhatIsMissing_SoASecondCallDuplicatesNothing()
    {
        var organizationId = Guid.NewGuid();
        var db = Connection(eligible: organizationId);

        await new OrganizationRepository(db).ProvisionForApplicationAsync(
            ExistingOrganizationRequest(organizationId), CancellationToken.None);

        db.Commands.Where(command => command.CommandText.Contains("INSERT INTO")).Should().HaveCount(2)
            .And.OnlyContain(command => Regex.IsMatch(command.CommandText, @"^\s*IF NOT EXISTS"),
                "the subscription and the role are inserted only where no row exists; an already enabled " +
                "application gets only the role row, and a repeat gets neither");

        db.Commands.Single(command => command.CommandText.Contains("UPDATE [dbo].[OrganizationApplications]"))
            .CommandText.Should().Contain("AND [IsActive] = 0", "an active subscription is left exactly as it is");
    }

    [Fact]
    public async Task SomeoneElsesOrganization_WritesNothing()
    {
        var db = Connection(eligible: null);

        var outcome = await new OrganizationRepository(db).ProvisionForApplicationAsync(
            ExistingOrganizationRequest(Guid.NewGuid()), CancellationToken.None);

        outcome.Status.Should().Be(OrganizationProvisioningStatus.OrganizationNotEligible);
        db.Commands.Should().NotContain(command =>
            command.CommandText.Contains("INSERT INTO") || command.CommandText.Contains("UPDATE [dbo]"));
        db.Transactions.Should().ContainSingle().Which.RolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task APostconditionThatDoesNotHold_RollsEverythingBack()
    {
        var db = Connection(setUpAfterWrites: 0);

        var outcome = await new OrganizationRepository(db).ProvisionForApplicationAsync(
            NewOrganizationRequest(), CancellationToken.None);

        outcome.Status.Should().Be(OrganizationProvisioningStatus.OrganizationNotEligible);
        db.Transactions.Should().ContainSingle().Which.Committed.Should().BeFalse();
        db.Transactions.Single().RolledBack.Should().BeTrue();
    }

    [Fact]
    public void TheMethod_BeginsItsTransactionOnTheOpenConnection_AndNeverOpensIt()
    {
        var body = ProvisionBody();

        body.Should().Contain("connection.BeginTransaction()");
        body.Should().NotContain(".Open(", "the factory hands back an open connection");
        foreach (var table in FourTables)
        {
            body.Should().Contain($"INSERT INTO [dbo].[{table}]", $"the method itself writes {table}");
        }
    }

    [Fact]
    public void TheSelfServiceCount_IsLockedAndSkipsPersonalOrganizations()
    {
        var body = ProvisionBody();
        var count = Regex.Match(body, @"SELECT COUNT\(1\) FROM \[dbo\]\.\[Organizations\] WITH \(UPDLOCK, HOLDLOCK\)\s+WHERE (?<where>[^""]+)""");

        count.Success.Should().BeTrue("two concurrent submits must not both pass a limit of one");
        count.Groups["where"].Value.Should().Contain("[OwnerId] = @UserId");
        count.Groups["where"].Value.Should().Contain("[IsAutoCreated] = 0",
            "a personal organization never counts toward the self-service limit");

        body.IndexOf("WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal).Should().BeLessThan(
            body.IndexOf("INSERT INTO", StringComparison.Ordinal), "the count is taken under the lock before any write");
    }

    [Fact]
    public void TheConsoleCount_SkipsPersonalOrganizations_Too()
    {
        var source = Source();
        var start = source.IndexOf("public async Task<int> CountSelfServiceOwnedAsync(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var body = source[start..source.IndexOf("/// <inheritdoc />", start, StringComparison.Ordinal)];

        body.Should().Contain("[OwnerId] = @UserId").And.Contain("[IsAutoCreated] = 0");
    }

    [Fact]
    public void AnExistingOrganization_MustBeTheUsersOwn_NotPersonal_AndActive()
    {
        var eligible = ConstantSql("EligibleOwnedOrganizationPredicate");

        eligible.Should().Contain("o.[OwnerId] = @UserId", "another user's organization is never set up");
        eligible.Should().Contain("o.[IsAutoCreated] = 0");
        eligible.Should().Contain("o.[IsActive] = 1");

        ProvisionBody().Should().MatchRegex(
            @"WITH \(UPDLOCK\)\s+WHERE o\.\[Id\] = @OrganizationId AND \{EligibleOwnedOrganizationPredicate\}");
    }

    [Fact]
    public void AuthorizeAndTheStep_ShareOnePredicate_SoTheyCannotLoop()
    {
        var source = Source();
        var predicate = ConstantSql("SetUpForApplicationPredicate");

        predicate.Should().Contain("o.[OwnerId] = @UserId");
        predicate.Should().Contain("[OrganizationApplications]");
        predicate.Should().Contain("our.[RoleId] = @CreatorRoleId");

        var find = source[source.IndexOf("FindOrganizationSetUpForApplicationAsync(", StringComparison.Ordinal)..];
        find[..find.IndexOf("/// <inheritdoc />", StringComparison.Ordinal)]
            .Should().Contain("{SetUpForApplicationPredicate}", "authorize asks the shared predicate");

        Regex.Matches(ProvisionBody(), @"\{SetUpForApplicationPredicate\}").Count.Should().Be(2,
            "the step asks it before writing and asserts it before committing");
    }

    [Fact]
    public void ACodeCollision_RollsBackAndAsksForAnotherCode()
    {
        ProvisionBody().Should().MatchRegex(
            @"catch \(Microsoft\.Data\.SqlClient\.SqlException ex\) when \(ex\.Number is 2601 or 2627\)\s*\{[^}]*Rollback\(\);[^}]*CodeTaken");
    }
}
