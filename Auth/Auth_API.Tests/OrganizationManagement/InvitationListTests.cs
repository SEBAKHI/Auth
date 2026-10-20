using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Application.Features.Organizations.GetPendingInvitations;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.Helpers;
using FluentValidation.TestHelper;

namespace Auth_API.Tests.OrganizationManagement;

/// <summary>
/// OI-193: an organization's invitation list shows every status, page by page. Expiry is
/// computed at read (nothing marks it in storage), by one rule on the entity that the list's
/// SQL repeats with the same clock reading; the list pages and sorts in SQL; accepted rows
/// carry who accepted and what became of the application role; and whether a row can be
/// resent is the server's answer, never the client's guess.
/// <para>
/// The repository is exercised through <see cref="RecordingDbConnectionFactory"/>: Dapper
/// builds the real commands and parameters, and the assertions read them. What the SQL
/// returns on a real server was run separately against a throwaway database (PR evidence).
/// </para>
/// </summary>
public class InvitationListTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    // --- T1: one effective status, on the entity ---

    [Theory]
    [InlineData(-1, InvitationStatus.Pending)]
    [InlineData(0, InvitationStatus.Expired)]
    [InlineData(1, InvitationStatus.Expired)]
    public void EffectiveStatus_OfAPendingInvitation_IsExpiredFromItsExpiryOn(
        int ticksPastExpiry, InvitationStatus expected)
    {
        var invitation = Invitation(InvitationStatus.Pending, expiresAt: Now.AddTicks(-ticksPastExpiry));

        invitation.EffectiveStatus(Now).Should().Be(expected,
            "the boundary belongs to Expired, as IsExpired() has always decided it (now >= ExpiresAt)");
    }

    [Theory]
    [InlineData(InvitationStatus.Accepted)]
    [InlineData(InvitationStatus.Declined)]
    [InlineData(InvitationStatus.Cancelled)]
    [InlineData(InvitationStatus.Expired)]
    public void EffectiveStatus_OfAnyOtherStoredStatus_IsThatStatus_BeforeAndAfterExpiry(InvitationStatus stored)
    {
        Invitation(stored, expiresAt: Now.AddDays(1)).EffectiveStatus(Now).Should().Be(stored);
        Invitation(stored, expiresAt: Now.AddDays(-1)).EffectiveStatus(Now).Should().Be(stored);
    }

    [Theory]
    [InlineData(InvitationStatus.Pending, true)]
    [InlineData(InvitationStatus.Accepted, false)]
    [InlineData(InvitationStatus.Declined, false)]
    [InlineData(InvitationStatus.Cancelled, false)]
    [InlineData(InvitationStatus.Expired, false)]
    public void CanBeResent_IsExactlyWhatRegenerateTokenAccepts(InvitationStatus stored, bool expected)
    {
        var invitation = Invitation(stored, expiresAt: Now.AddDays(-1));

        invitation.CanBeResent().Should().Be(expected);
        invitation.RegenerateToken("new-hash").IsError.Should().Be(!expected);
    }

    // --- T2 + T3 + T4: the repository query ---

    [Fact]
    public async Task Paged_WithoutStatuses_ReadsTheStoredPendingRows_AsTheListAlwaysDid()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await Page(db, InvitationListFilter.StoredPending);

        var (count, page) = CountAndPage(db);
        foreach (var command in new[] { count, page })
        {
            command.CommandText.Should().MatchRegex(
                @"WHERE\s+i\.\[OrganizationId\]\s*=\s*@OrganizationId\s+AND\s+i\.\[Status\]\s*=\s*'Pending'\s*(ORDER|$)",
                "no status is the list's historical content: stored-pending rows, live or expired");
            command.CommandText.Should().NotContain("@Statuses");
        }
    }

    [Fact]
    public async Task Paged_WithStatuses_MatchesTheEffectiveStatus_InCountAndPage_AgainstTheCallersNow()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await Page(db, new InvitationListFilter([InvitationStatus.Expired, InvitationStatus.Accepted]));

        var (count, page) = CountAndPage(db);
        var where = Where(count);
        Where(page).Should().Be(where, "the count and the page share one WHERE clause");
        where.Should().Contain(
            "CASE WHEN i.[Status] = 'Pending' AND i.[ExpiresAt] <= @Now THEN 'Expired' ELSE i.[Status] END IN",
            "Expired means stored Expired, or stored Pending at or past its expiry; Pending means "
            + "stored Pending before it; every other status is itself");
        foreach (var command in new[] { count, page })
        {
            command.Parameters["Now"].Should().Be(Now, "the caller's clock, never the server's");
            command.Parameters.Values.Should().Contain("Expired").And.Contain("Accepted",
                "statuses are bound as parameters by name, as they are stored");
            command.CommandText.Should().NotContain("GETUTCDATE", "one clock decides expiry");
        }
    }

    [Theory]
    [InlineData(new[] { InvitationStatus.Pending }, new[] { "Pending" })]
    [InlineData(new[] { InvitationStatus.Expired }, new[] { "Pending", "Expired" })]
    [InlineData(new[] { InvitationStatus.Accepted, InvitationStatus.Declined }, new[] { "Accepted", "Declined" })]
    public async Task Paged_WithStatuses_NarrowsByAStoredStatusSuperset_AnIndexCanSeek(
        InvitationStatus[] statuses, string[] stored)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await Page(db, new InvitationListFilter(statuses));

        var count = CountAndPage(db).Count;
        Where(count).Should().Contain("i.[Status] IN (@StoredStatuses",
            "an effective status is its own stored status, or stored Pending for Expired");
        count.Parameters
            .Where(parameter => parameter.Key.StartsWith("StoredStatuses", StringComparison.Ordinal))
            .Select(parameter => parameter.Value)
            .Should().BeEquivalentTo(stored);
    }

    [Fact]
    public void TheEffectiveStatusSql_IsTheEntitysRule()
    {
        // The same three outcomes EffectiveStatus has: stored Pending before, stored Pending at
        // or after, anything else. A drift between the two would show an invitation as live on
        // the list and expired on its own page, or the other way round.
        OrganizationRepository.InvitationEffectiveStatusSql.Should().Be(
            "CASE WHEN i.[Status] = 'Pending' AND i.[ExpiresAt] <= @Now THEN 'Expired' ELSE i.[Status] END");
    }

    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 50, 100)]
    public async Task Paged_FetchesTheRequestedPage(int pageNumber, int pageSize, int offset)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await Page(db, InvitationListFilter.StoredPending, pageNumber: pageNumber, pageSize: pageSize);

        var page = CountAndPage(db).Page;
        page.CommandText.Should().MatchRegex(@"OFFSET\s+@Offset\s+ROWS\s+FETCH\s+NEXT\s+@PageSize\s+ROWS\s+ONLY");
        page.Parameters["Offset"].Should().Be(offset);
        page.Parameters["PageSize"].Should().Be(pageSize);
    }

    [Fact]
    public async Task Paged_WithoutSortBy_IsNewestFirst_WithTheIdAsTieBreaker()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await Page(db, InvitationListFilter.StoredPending, sortBy: null);

        CountAndPage(db).Page.CommandText.Should().MatchRegex(@"ORDER BY\s+i\.\[CreatedAt\] DESC,\s*i\.\[Id\]\s");
    }

    public static TheoryData<string, SortDirection> EveryAllowedSort()
    {
        var data = new TheoryData<string, SortDirection>();
        foreach (var field in SortFields.OrganizationInvitations.Allowed)
        {
            data.Add(field, SortDirection.Asc);
            data.Add(field, SortDirection.Desc);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryAllowedSort))]
    public async Task Paged_SortsEveryAllowListedField_InSql(string sortBy, SortDirection direction)
    {
        // An allow-listed field the map forgot falls back to the default order without an
        // error: the 200 would look right and sort nothing. So every field must reach SQL.
        OrganizationRepository.InvitationSortColumns.Should().ContainKey(sortBy);
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await Page(db, InvitationListFilter.StoredPending, sortBy: sortBy, direction: direction);

        var keyword = direction == SortDirection.Desc ? "DESC" : "ASC";
        var expected = string.Join(", ",
            OrganizationRepository.InvitationSortColumns[sortBy].Select(column => $"{column} {keyword}"));
        CountAndPage(db).Page.CommandText.Should().Contain($"ORDER BY {expected}, i.[Id]");
    }

    [Fact]
    public void TheSortMap_CoversTheAllowList_AndNothingElse()
    {
        OrganizationRepository.InvitationSortColumns.Keys
            .Should().BeEquivalentTo(SortFields.OrganizationInvitations.Allowed,
                "a previously valid sortBy must not become a 400, nor silently stop sorting");
        SortFields.OrganizationInvitations.Allowed.Should().HaveCount(10, "the allow-list does not shrink");
    }

    // --- T7: the duplicate check keeps its rule ---

    [Fact]
    public async Task PendingInvitations_TheDuplicateChecksQuery_StaysStoredPending_ExpiredOrNot()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new OrganizationRepository(db).GetPendingInvitationsAsync(Guid.NewGuid(), CancellationToken.None);

        var command = db.Commands.Single();
        command.CommandText.Should().MatchRegex(@"WHERE\s+\[OrganizationId\]\s*=\s*@OrganizationId\s+AND\s+\[Status\]\s*=\s*'Pending'\s+ORDER");
        command.CommandText.Should().NotContain("ExpiresAt] <",
            "a stored-pending invitation past its expiry still blocks a second invitation to the same address");
    }

    // --- T3 + T5 + T6: the handler ---

    [Fact]
    public async Task Handler_SendsTheFilterThePageAndOneClockReading_AndReturnsThePageShape()
    {
        var fixture = new HandlerFixture();
        fixture.Returns([Invitation(InvitationStatus.Pending, Now.AddDays(1))], totalCount: 41);

        var result = await fixture.Handle(new GetPendingInvitationsQuery(
            fixture.OrganizationId, [InvitationStatus.Accepted, InvitationStatus.Expired], PageNumber: 3, PageSize: 20,
            SortBy: "email", SortDirection: SortDirection.Desc));

        result.IsError.Should().BeFalse();
        fixture.Repository.Verify(r => r.GetInvitationsPagedAsync(
            fixture.OrganizationId,
            It.Is<InvitationListFilter>(f => f.Statuses.SequenceEqual(new[] { InvitationStatus.Accepted, InvitationStatus.Expired })),
            Now, 3, 20, "email", SortDirection.Desc, It.IsAny<CancellationToken>()), Times.Once);
        result.Value.TotalCount.Should().Be(41);
        result.Value.PageNumber.Should().Be(3);
        result.Value.PageSize.Should().Be(20);
        result.Value.TotalPages.Should().Be(3);
        result.Value.HasPreviousPage.Should().BeTrue();
        result.Value.HasNextPage.Should().BeFalse("page 3 of 41 rows at 20 a page is the short last page");
    }

    [Fact]
    public async Task Handler_WithoutStatuses_AsksForTheStoredPendingList()
    {
        var fixture = new HandlerFixture();
        fixture.Returns([], totalCount: 0);

        await fixture.Handle(new GetPendingInvitationsQuery(fixture.OrganizationId));

        fixture.Repository.Verify(r => r.GetInvitationsPagedAsync(
            fixture.OrganizationId,
            It.Is<InvitationListFilter>(f => f.Statuses.Count == 0),
            Now, 1, 20, null, SortDirection.Asc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(InvitationStatus.Pending, 1, "Pending", false, true)]
    [InlineData(InvitationStatus.Pending, -1, "Expired", true, true)]
    [InlineData(InvitationStatus.Expired, 1, "Expired", true, false)]
    [InlineData(InvitationStatus.Accepted, -1, "Accepted", false, false)]
    [InlineData(InvitationStatus.Cancelled, -1, "Cancelled", false, false)]
    [InlineData(InvitationStatus.Declined, -1, "Declined", false, false)]
    public async Task Handler_GivesEachRowItsEffectiveStatus_AndWhetherItCanBeResent(
        InvitationStatus stored, int daysToExpiry, string status, bool isExpired, bool canResend)
    {
        var fixture = new HandlerFixture();
        fixture.Returns([Invitation(stored, Now.AddDays(daysToExpiry))], totalCount: 1);

        var row = (await fixture.Handle(new GetPendingInvitationsQuery(fixture.OrganizationId)))
            .Value.Invitations.Single();

        row.Status.Should().Be(status);
        row.IsExpired.Should().Be(isExpired, "IsExpired is true exactly when the effective status is Expired");
        row.CanResend.Should().Be(canResend,
            "resend accepts a stored-pending invitation, expired or not, and nothing else");
    }

    [Theory]
    [InlineData(InvitationApplicationRoleOutcome.Applied)]
    [InlineData(InvitationApplicationRoleOutcome.AlreadyHeld)]
    [InlineData(InvitationApplicationRoleOutcome.Dropped)]
    public async Task Handler_AnAcceptedRow_CarriesWhoAcceptedWhen_AndTheApplicationRoleOutcome(
        InvitationApplicationRoleOutcome outcome)
    {
        var fixture = new HandlerFixture();
        var accepter = TestHelpers.CreateUser(firstName: "Ada", lastName: "Accepter");
        var acceptedAt = Now.AddHours(-3);
        var invitation = new OrganizationInvitation(
            Guid.NewGuid(), fixture.OrganizationId, "invitee@example.com", Guid.NewGuid(), "hash",
            InvitationStatus.Accepted, Now.AddDays(-1), Guid.NewGuid(), acceptedAt, accepter.Id, Now.AddDays(-8),
            InvitationApplicationRole.From(Guid.NewGuid(), Guid.NewGuid()), outcome);
        fixture.Returns([invitation], totalCount: 1);
        fixture.Users.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { accepter });

        var row = (await fixture.Handle(new GetPendingInvitationsQuery(fixture.OrganizationId)))
            .Value.Invitations.Single();

        row.AcceptedAt.Should().Be(acceptedAt);
        row.AcceptedByUserId.Should().Be(accepter.Id);
        row.AcceptedByUserName.Should().Be("Ada Accepter");
        row.ApplicationRoleOutcome.Should().Be(outcome);
    }

    [Fact]
    public void TheOutcome_IsWrittenByName_AndOmittedWhenNull_AsTheApiSerializes()
    {
        // The API's own options (Program.cs AddJsonOptions): enums by member name, nulls omitted.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        var applied = JsonSerializer.Serialize(new Auth.Application.DTOs.OrganizationInvitationDto
        {
            ApplicationRoleOutcome = InvitationApplicationRoleOutcome.Dropped,
            CanResend = false,
        }, options);
        var membershipOnly = JsonSerializer.Serialize(new Auth.Application.DTOs.OrganizationInvitationDto(), options);

        applied.Should().Contain("\"applicationRoleOutcome\":\"Dropped\"").And.Contain("\"canResend\":false");
        membershipOnly.Should().NotContain("applicationRoleOutcome");
    }

    // --- T3: the validator ---

    [Theory]
    [InlineData(0, "Paging.PageSizeOutOfRange")]
    [InlineData(101, "Paging.PageSizeOutOfRange")]
    public void Validator_RefusesAPageSizeOutsideOneToAHundred_WithThePagingCode(int pageSize, string code)
    {
        new GetPendingInvitationsQueryValidator()
            .TestValidate(new GetPendingInvitationsQuery(Guid.NewGuid(), PageSize: pageSize))
            .ShouldHaveValidationErrorFor(q => q.PageSize).WithErrorCode(code);
    }

    [Fact]
    public void Validator_RefusesPageZero_WithThePagingCode()
    {
        new GetPendingInvitationsQueryValidator()
            .TestValidate(new GetPendingInvitationsQuery(Guid.NewGuid(), PageNumber: 0))
            .ShouldHaveValidationErrorFor(q => q.PageNumber).WithErrorCode("Paging.PageNumberOutOfRange");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Validator_AcceptsTheBoundaryPageSizes(int pageSize)
    {
        new GetPendingInvitationsQueryValidator()
            .TestValidate(new GetPendingInvitationsQuery(Guid.NewGuid(), PageSize: pageSize))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [MemberData(nameof(EveryAllowedSort))]
    public void Validator_AcceptsEveryAllowListedSort(string sortBy, SortDirection direction)
    {
        new GetPendingInvitationsQueryValidator()
            .TestValidate(new GetPendingInvitationsQuery(Guid.NewGuid(), SortBy: sortBy, SortDirection: direction))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_StillRefusesAnUnknownSort()
    {
        new GetPendingInvitationsQueryValidator()
            .TestValidate(new GetPendingInvitationsQuery(Guid.NewGuid(), SortBy: "token"))
            .ShouldHaveValidationErrorFor(q => q.SortBy);
    }

    #region Helpers

    private static OrganizationInvitation Invitation(InvitationStatus status, DateTime expiresAt) =>
        TestHelpers.CreateOrganizationInvitation(status: status, expiresAt: expiresAt);

    private static Task Page(
        RecordingDbConnectionFactory db,
        InvitationListFilter filter,
        int pageNumber = 1,
        int pageSize = 20,
        string? sortBy = null,
        SortDirection direction = SortDirection.Asc) =>
        new OrganizationRepository(db).GetInvitationsPagedAsync(
            Guid.NewGuid(), filter, Now, pageNumber, pageSize, sortBy, direction, CancellationToken.None);

    private static (RecordedCommand Count, RecordedCommand Page) CountAndPage(RecordingDbConnectionFactory db)
    {
        db.Commands.Should().HaveCount(2, "a count, then the page");
        db.Commands[0].CommandText.Should().Contain("COUNT(1)");
        return (db.Commands[0], db.Commands[1]);
    }

    private static string Where(RecordedCommand command)
    {
        var text = command.CommandText;
        var from = text.IndexOf("WHERE", StringComparison.Ordinal);
        var to = text.IndexOf("ORDER BY", from, StringComparison.Ordinal);
        return (to < 0 ? text[from..] : text[from..to]).Trim();
    }

    private sealed class HandlerFixture
    {
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public Mock<IOrganizationRepository> Repository { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        private readonly Mock<IRoleRepository> _roles = new();

        public HandlerFixture()
        {
            Repository.Setup(r => r.GetByIdAsync(OrganizationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(TestHelpers.CreateOrganization(id: OrganizationId, isActive: true));
            _roles.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TestHelpers.CreateRole());
            Users.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<User>());
        }

        public void Returns(IReadOnlyList<OrganizationInvitation> invitations, int totalCount) =>
            Repository
                .Setup(r => r.GetInvitationsPagedAsync(
                    It.IsAny<Guid>(), It.IsAny<InvitationListFilter>(), It.IsAny<DateTime>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<SortDirection>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((invitations, totalCount));

        public Task<ErrorOr.ErrorOr<Auth.Application.DTOs.PagedOrganizationInvitationsDto>> Handle(
            GetPendingInvitationsQuery query) =>
            new GetPendingInvitationsQueryHandler(
                    Repository.Object,
                    Users.Object,
                    _roles.Object,
                    new Mock<IApplicationRepository>().Object,
                    ApplicationTestImages.Composer(),
                    new FixedTimeProvider(new DateTimeOffset(Now)))
                .Handle(query with { PlatformScope = true }, CancellationToken.None);
    }

    #endregion
}
