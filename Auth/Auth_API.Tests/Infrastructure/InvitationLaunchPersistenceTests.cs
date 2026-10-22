using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-192 through Dapper: the two new columns, what the repositories send and read back. A
/// recording connection stands in for the database, so Dapper builds the real commands and
/// parameters and maps a row shaped like the table's. The table scripts and the acceptance
/// statement also ran, as written, against a throwaway LocalDB database, where the CHECK that
/// ties an outcome to an application role refused the row this repository never writes (PR
/// evidence).
/// </summary>
public class InvitationLaunchPersistenceTests
{
    // --- OrganizationInvitations.ApplicationRoleOutcome ---

    [Theory]
    [InlineData(InvitationApplicationRoleOutcome.Applied, "Applied")]
    [InlineData(InvitationApplicationRoleOutcome.AlreadyHeld, "AlreadyHeld")]
    [InlineData(InvitationApplicationRoleOutcome.Dropped, "Dropped")]
    public async Task AcceptInvitationAsync_WritesTheOutcomeInTheStatusFlip_InTheTransaction(
        InvitationApplicationRoleOutcome outcome, string stored)
    {
        var invitation = TestHelpers.CreateOrganizationInvitation(
            applicationRole: InvitationApplicationRole.From(Guid.NewGuid(), Guid.NewGuid()));
        invitation.Accept(Guid.NewGuid(), outcome);
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        var accepted = await new OrganizationRepository(db).AcceptInvitationAsync(
            invitation, membership: null, applicationRole: null, CancellationToken.None);

        accepted.Should().BeTrue();
        var flip = db.Commands.Single(c => c.CommandText.Contains("UPDATE [dbo].[OrganizationInvitations]", StringComparison.Ordinal));
        flip.CommandText.Should().MatchRegex(@"\[ApplicationRoleOutcome\]\s*=\s*@ApplicationRoleOutcome");
        flip.CommandText.Should().MatchRegex(@"WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[Status\]\s*=\s*'Pending'",
            "the outcome lands only with the acceptance that wins");
        flip.Parameters["ApplicationRoleOutcome"].Should().Be(stored, "stored by name, as the CHECK lists them");
        flip.InTransaction.Should().BeTrue();
        db.LastTransaction!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptInvitationAsync_MembershipOnly_WritesNoOutcome()
    {
        var invitation = TestHelpers.CreateOrganizationInvitation();
        invitation.Accept(Guid.NewGuid());
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new OrganizationRepository(db).AcceptInvitationAsync(
            invitation, membership: null, applicationRole: null, CancellationToken.None);

        db.Commands.First().Parameters["ApplicationRoleOutcome"].Should().BeNull();
    }

    [Theory]
    [InlineData("Applied", InvitationApplicationRoleOutcome.Applied)]
    [InlineData("AlreadyHeld", InvitationApplicationRoleOutcome.AlreadyHeld)]
    [InlineData("Dropped", InvitationApplicationRoleOutcome.Dropped)]
    [InlineData(null, null)]
    public async Task GetInvitationByTokenHashAsync_ReadsTheOutcome(
        string? stored, InvitationApplicationRoleOutcome? outcome)
    {
        var applicationId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                Email = "rep@example.com",
                RoleId = Guid.NewGuid(),
                Token = "hash",
                Status = "Accepted",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                InvitedBy = Guid.NewGuid(),
                AcceptedAt = (DateTime?)DateTime.UtcNow,
                AcceptedByUserId = (Guid?)Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                ApplicationId = (Guid?)applicationId,
                ApplicationRoleId = (Guid?)Guid.NewGuid(),
                ApplicationRoleOutcome = stored
            });

        var invitation = await new OrganizationRepository(db).GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(CredentialPurpose.Invitation, "hash"), CancellationToken.None);

        db.LastCommand!.CommandText.Should().Contain("[ApplicationId], [ApplicationRoleId], [ApplicationRoleOutcome]");
        invitation!.ApplicationRoleOutcome.Should().Be(outcome);
    }

    public static TheoryData<string> OtherInvitationReads => new()
    {
        nameof(OrganizationRepository.GetInvitationByIdAsync),
        nameof(OrganizationRepository.GetPendingInvitationsAsync),
        nameof(OrganizationRepository.GetPendingInvitationsByEmailAsync),
    };

    [Theory]
    [MemberData(nameof(OtherInvitationReads))]
    public async Task EveryInvitationRead_ReadsTheOutcome(string read)
    {
        // The invitation lists (OI-193) show the outcome: a read that forgot the column
        // would show every accepted invitation as having none.
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: _ => new
            {
                Id = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                Email = "rep@example.com",
                RoleId = Guid.NewGuid(),
                Token = "hash",
                Status = "Accepted",
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                InvitedBy = Guid.NewGuid(),
                AcceptedAt = (DateTime?)DateTime.UtcNow,
                AcceptedByUserId = (Guid?)Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                ApplicationId = (Guid?)Guid.NewGuid(),
                ApplicationRoleId = (Guid?)Guid.NewGuid(),
                ApplicationRoleOutcome = "Dropped"
            });
        var repository = new OrganizationRepository(db);

        var invitation = read switch
        {
            nameof(OrganizationRepository.GetInvitationByIdAsync) =>
                await repository.GetInvitationByIdAsync(Guid.NewGuid(), CancellationToken.None),
            nameof(OrganizationRepository.GetPendingInvitationsAsync) =>
                (await repository.GetPendingInvitationsAsync(Guid.NewGuid(), CancellationToken.None)).Single(),
            _ => (await repository.GetPendingInvitationsByEmailAsync("rep@example.com", CancellationToken.None)).Single(),
        };

        db.LastCommand!.CommandText.Should().Contain("[ApplicationId], [ApplicationRoleId], [ApplicationRoleOutcome]");
        invitation!.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Dropped);
    }

    // --- Applications.InitiateLoginUri ---

    private const string Start = "https://crm.example.com/signin/start";

    [Fact]
    public async Task ApplicationCreateAsync_InsertsTheInitiateLoginUri()
    {
        var application = TestHelpers.CreateApplication(code: "CRM");
        application.SetInitiateLoginUri(Start, Guid.NewGuid());
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).CreateAsync(application, CancellationToken.None);

        var insert = db.Commands.Single(c => c.CommandText.Contains("INSERT INTO [dbo].[Applications]", StringComparison.Ordinal));
        insert.CommandText.Should().Contain("[LogoUrlDark], [InitiateLoginUri],").And.Contain("@LogoUrlDark, @InitiateLoginUri,");
        insert.Parameters["InitiateLoginUri"].Should().Be(Start);
    }

    [Fact]
    public async Task ApplicationUpdateAsync_WritesTheInitiateLoginUri()
    {
        var application = TestHelpers.CreateApplication(code: "CRM");
        application.SetInitiateLoginUri(Start, Guid.NewGuid());
        var db = new RecordingDbConnectionFactory(affectedRows: 1);

        await new ApplicationRepository(db).UpdateAsync(application, CancellationToken.None);

        var update = db.Commands.Single(c => c.CommandText.Contains("UPDATE [dbo].[Applications] SET", StringComparison.Ordinal));
        update.CommandText.Should().MatchRegex(@"\[InitiateLoginUri\]\s*=\s*@InitiateLoginUri");
        update.Parameters["InitiateLoginUri"].Should().Be(Start);
    }

    [Fact]
    public async Task ApplicationGetByIdAsync_ReadsTheInitiateLoginUri()
    {
        var db = new RecordingDbConnectionFactory(
            affectedRows: 0,
            rowFor: command => command.CommandText.Contains("FROM [dbo].[Applications]", StringComparison.Ordinal)
                ? new
                {
                    Id = Guid.NewGuid(),
                    Code = "CRM",
                    Name = "CRM",
                    IsActive = true,
                    SessionTimeoutMinutes = 60,
                    MaxConcurrentSessions = 5,
                    AccessMode = (byte)1,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = Guid.NewGuid(),
                    InitiateLoginUri = Start
                }
                : null);

        var application = await new ApplicationRepository(db).GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        db.Commands.First().CommandText.Should().Contain("[LogoUrlDark], [InitiateLoginUri],");
        application!.InitiateLoginUri.Should().Be(Start);
    }
}
