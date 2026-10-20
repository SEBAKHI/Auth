using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ReadModels.Organizations;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Authorization;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.OrganizationManagement.ApplicationDirectory;

/// <summary>
/// The application directory's storage and vocabulary (OI-194): the SQL the repository builds
/// (read through <see cref="RecordingDbConnectionFactory"/>, so Dapper builds the real commands;
/// what that SQL returns on a real server was run separately against a throwaway database, PR
/// evidence), the new scope, and the matching rule the handlers use.
/// </summary>
public class ApplicationDirectoryQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

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

    // --- V5: an application's invitations only ---

    [Fact]
    public async Task Invitations_WithAnApplication_AreNarrowedToItsRows_InCountAndPage()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var applicationId = Guid.NewGuid();

        await new OrganizationRepository(db).GetInvitationsPagedAsync(
            Guid.NewGuid(), new InvitationListFilter([InvitationStatus.Pending, InvitationStatus.Expired], applicationId),
            Now, 1, 20, null, SortDirection.Asc, CancellationToken.None);

        var (count, page) = CountAndPage(db);
        Where(page).Should().Be(Where(count), "the count and the page share one WHERE clause");
        Where(count).Should().EndWith("AND i.[ApplicationId] = @ApplicationId",
            "a membership-only invitation (NULL) and another application's never match");
        count.Parameters["ApplicationId"].Should().Be(applicationId);
        page.Parameters["ApplicationId"].Should().Be(applicationId);
    }

    [Fact]
    public async Task Invitations_WithoutAnApplication_AreTheOrganizationsWholeList_AsBefore()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new OrganizationRepository(db).GetInvitationsPagedAsync(
            Guid.NewGuid(), InvitationListFilter.StoredPending, Now, 1, 20, null, SortDirection.Asc, CancellationToken.None);

        foreach (var command in new[] { CountAndPage(db).Count, CountAndPage(db).Page })
        {
            command.CommandText.Should().NotContain("[ApplicationId] = @ApplicationId",
                "the console's own list is unchanged by the application filter");
        }
    }

    // --- V8 + T8: the members an application may see ---

    [Fact]
    public async Task Members_AreActiveUnexpiredAndNotDeleted_AtTheCallersNow_InCountAndPage_OldestFirst()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new OrganizationRepository(db).GetActiveMembersPagedAsync(Guid.NewGuid(), Now, 3, 50, CancellationToken.None);

        var (count, page) = CountAndPage(db);
        Where(page).Should().Be(Where(count));
        var where = Where(count);
        where.Should().Contain("ou.[IsActive] = 1");
        where.Should().Contain("(ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > @Now)", "a lapsed membership is not listed");
        where.Should().Contain("u.[IsDeleted] = 0", "an application never receives a deleted account");
        foreach (var command in new[] { count, page })
        {
            command.Parameters["Now"].Should().Be(Now, "the caller's clock, never the server's");
            command.ParameterTypes["Now"].Should().Be(System.Data.DbType.DateTime2);
            command.CommandText.Should().NotContain("GETUTCDATE");
        }

        page.CommandText.Should().MatchRegex(@"ORDER BY ou\.\[JoinedAt\], ou\.\[Id\]\s+OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
        page.Parameters["Offset"].Should().Be(100);
        page.Parameters["PageSize"].Should().Be(50);
        page.CommandText.Should().Contain("r.[Code] AS [MembershipRoleCode]");
    }

    [Fact]
    public async Task Members_TheLargestPageNumberTheValidatorAccepts_StillFitsTheOffset()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new OrganizationRepository(db).GetActiveMembersPagedAsync(
            Guid.NewGuid(), Now, ApplicationDirectoryPaging.MaxPageNumber, 100, CancellationToken.None);

        CountAndPage(db).Page.Parameters["Offset"].Should().Be((ApplicationDirectoryPaging.MaxPageNumber - 1) * 100);
    }

    [Fact]
    public async Task ApplicationRoles_AreThisApplicationsActiveUnexpiredRoles_ForThePageOnly_InOneQuery()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var applicationId = Guid.NewGuid();
        Guid[] users = [Guid.NewGuid(), Guid.NewGuid()];

        await new OrganizationRepository(db).GetMemberApplicationRolesAsync(
            Guid.NewGuid(), applicationId, users, Now, CancellationToken.None);

        var command = db.Commands.Should().ContainSingle("the whole page in one query").Subject;
        var where = Where(command);
        where.Should().Contain("our.[ApplicationId] = @ApplicationId");
        where.Should().Contain("r.[ApplicationId] = @ApplicationId", "a role of another application is never reported");
        where.Should().Contain("our.[IsActive] = 1");
        where.Should().Contain("(our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > @Now)");
        where.Should().MatchRegex(@"our\.\[UserId\] IN \(@UserIds1,\s*@UserIds2\)");
        command.Parameters["ApplicationId"].Should().Be(applicationId);
        command.ParameterTypes["Now"].Should().Be(System.Data.DbType.DateTime2);
    }

    [Fact]
    public async Task ApplicationRoles_ForAnEmptyPage_AskNothing()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        var rows = await new OrganizationRepository(db).GetMemberApplicationRolesAsync(
            Guid.NewGuid(), Guid.NewGuid(), [], Now, CancellationToken.None);

        rows.Should().BeEmpty();
        db.Commands.Should().BeEmpty();
    }

    // --- V9: the scope ---

    [Fact]
    public void TheScope_IsSupported_Optional_AndLastInCanonicalOrder()
    {
        OAuthScopes.OrganizationsRead.Should().Be("organizations.read");
        OAuthScopes.Supported.Should().Equal("openid", "profile", "email", "phone", "organizations.read");
        OAuthScopes.Optional.Should().Equal("profile", "email", "phone", "organizations.read");
    }

    [Fact]
    public void TheScope_Parses_AndCanonicalises()
    {
        ScopeSet.TryParse("organizations.read openid email", out var scopes).Should().Be(ScopeSet.ParseStatus.Parsed);
        scopes.Value.Should().Be("openid email organizations.read");
        ScopeSet.TryParse("Organizations.Read", out _).Should().Be(ScopeSet.ParseStatus.Unknown, "scope names are case-sensitive");
        scopes.Value.Length.Should().BeLessThanOrEqualTo(200, "the stored grant columns are NVARCHAR(200)");
        string.Join(' ', OAuthScopes.Supported).Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public void TheGrant_GivesTheScopeOnlyWhenAllowed_AndARefreshNarrowsItAfterRemoval()
    {
        // The grant rule authorize, the code exchange and the refresh apply: requested ∩ allowed.
        ScopeSet.TryParse("openid organizations.read", out var requested);
        var allowed = ScopeSet.FromNames([OAuthScopes.Profile, OAuthScopes.OrganizationsRead]).Value;
        var notAllowed = ScopeSet.FromNames([OAuthScopes.Profile]).Value;

        requested.Intersect(allowed).Contains(OAuthScopes.OrganizationsRead).Should().BeTrue();
        requested.Intersect(notAllowed).Contains(OAuthScopes.OrganizationsRead).Should().BeFalse();

        var stored = ScopeSet.FromStored("openid organizations.read");
        stored.Intersect(notAllowed).Value.Should().Be("openid", "a refresh after removal never keeps it");
    }

    [Fact]
    public void AnApplicationNobodyConfigured_IsNotAllowedTheScope()
    {
        TestHelpers.CreateApplication().AllowedScopes.Contains(OAuthScopes.OrganizationsRead).Should().BeFalse(
            "AllowedScopes NULL is openid only: no application holds it until an administrator allows it");
    }

    // --- T6: the grant's matching rule is the permission gate's ---

    public static TheoryData<string[], string> MatchCases => new()
    {
        { ["org:*"], PermissionCodes.Org.MembersRead },
        { ["*"], PermissionCodes.Org.MembersInvite },
        { ["org:members:*"], PermissionCodes.Org.MembersInvite },
        { ["org:members:read"], PermissionCodes.Org.MembersInvite },
        { ["org:members:read"], PermissionCodes.Org.MembersRead },
        { ["ORG:MEMBERS:READ"], PermissionCodes.Org.MembersRead },
        { ["org:permissions:*"], PermissionCodes.Org.MembersRead },
        { ["org:members"], PermissionCodes.Org.MembersRead },
        { [], PermissionCodes.Org.PermissionsRead },
    };

    [Fact]
    public void MatchCases_IsNotEmpty() => MatchCases.Count<object[]>().Should().BeGreaterThan(5);

    [Theory]
    [MemberData(nameof(MatchCases))]
    public void TheGrant_MatchesExactlyAsThePermissionGateDoes(string[] held, string required)
    {
        var grant = new ApplicationDirectoryGrant(
            TestHelpers.CreateOrganization(), TestHelpers.CreateApplication(), Guid.NewGuid(), held);

        grant.Holds(required).Should().Be(PermissionRequirementHandler.PermissionMatches(held, required));
    }
}
