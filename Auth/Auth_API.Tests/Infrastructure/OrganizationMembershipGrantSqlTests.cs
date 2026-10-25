using System.Text.RegularExpressions;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-231, pinned in CI on the statements the repositories send: removing a member deletes
/// what the membership held in one batch (V1); an organization application role reaches a
/// token, and the user page, only through a live membership in an active organization that
/// has the application enabled (V4); the in-organization grant check reads only a member's
/// rows (V5); invitation acceptance writes the role through the assignment's update-or-insert
/// (V6); and the assignment checks the membership inside its own statement (V8). A throwaway
/// LocalDB run of the same repositories is in the PR.
/// </summary>
/// <remarks>
/// Deliberate breaks: drop the role delete from the removal; drop the live-membership joins
/// from the token read, or turn one of them into a LEFT JOIN; drop the membership join from
/// one branch of the grant check, or turn it into a LEFT JOIN; restore a plain INSERT in
/// acceptance; drop a column from the upsert's restore; drop the membership check from the
/// assignment.
/// </remarks>
public class OrganizationMembershipGrantSqlTests
{
    private static string Normalized(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

    private static void ContainsInOrder(string sql, params string[] steps)
    {
        var at = 0;
        foreach (var step in steps)
        {
            var found = sql.IndexOf(step, at, StringComparison.Ordinal);
            found.Should().BeGreaterThanOrEqualTo(0, $"the statement must contain, in order: {step}");
            at = found + step.Length;
        }
    }

    /// <summary>
    /// The clauses of a FROM … WHERE section: each table, join condition and predicate. A
    /// joined table keeps its join keyword ("INNER JOIN [dbo].[OrganizationUsers] ou"): a gate
    /// turned into a LEFT JOIN lets every row through, and must not read as the same clause.
    /// </summary>
    private static string[] Clauses(string section) =>
        Regex.Split(" " + section, @"\s+(?:FROM|ON|WHERE|AND)\s+|\s+(?=(?:INNER|LEFT|RIGHT|FULL|CROSS)(?:\s+OUTER)?\s+JOIN\s)")
            .Select(clause => clause.Trim().TrimEnd(';'))
            .Where(clause => clause.Length > 0)
            .ToArray();

    // ---- V1: removal ---------------------------------------------------------------

    [Fact]
    public async Task RemoveMember_DeletesTheGrantsThenTheMembership_InOneBatch_AndSparesTheOwner()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new ResultSets(
            new { ApplicationId = Guid.NewGuid(), RoleId = Guid.NewGuid(), RoleCode = "crm:agent", RoleName = "Agent" },
            new { ApplicationId = Guid.NewGuid(), PermissionId = Guid.NewGuid(), PermissionCode = "crm:tickets:read" },
            new { MembershipRemoved = true }));

        await new OrganizationRepository(db).RemoveMemberAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var sql = Normalized(db.Commands.Should().ContainSingle("one batch: all of it or none of it").Subject.CommandText);
        ContainsInOrder(sql,
            "SET XACT_ABORT ON; BEGIN TRANSACTION;",
            // The owner check holds the organization row first (an ownership transfer waits).
            "SELECT 1 FROM [dbo].[Organizations] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @OrganizationId AND [OwnerId] <> @UserId)",
            "SELECT 1 FROM [dbo].[OrganizationUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId) SET @Removable = 1;",
            // The column names are what the answer maps by: a renamed alias would leave the
            // audit row without the codes.
            "DELETE our OUTPUT deleted.[ApplicationId], deleted.[RoleId], r.[Code] AS [RoleCode], r.[Name] AS [RoleName]",
            "FROM [dbo].[OrganizationUserRoles] our",
            "LEFT JOIN [dbo].[Roles] r ON r.[Id] = our.[RoleId]",
            "WHERE our.[OrganizationId] = @OrganizationId AND our.[UserId] = @UserId AND @Removable = 1;",
            "DELETE oup OUTPUT deleted.[ApplicationId], deleted.[PermissionId], p.[Code] AS [PermissionCode]",
            "FROM [dbo].[OrganizationUserPermissions] oup",
            "LEFT JOIN [dbo].[Permissions] p ON p.[Id] = oup.[PermissionId]",
            "WHERE oup.[OrganizationId] = @OrganizationId AND oup.[UserId] = @UserId AND @Removable = 1;",
            "DELETE ou FROM [dbo].[OrganizationUsers] ou WHERE ou.[OrganizationId] = @OrganizationId AND ou.[UserId] = @UserId AND @Removable = 1",
            "AND NOT EXISTS ( SELECT 1 FROM [dbo].[Organizations] o WHERE o.[Id] = ou.[OrganizationId] AND o.[OwnerId] = ou.[UserId]);",
            "COMMIT TRANSACTION; SELECT @MembershipRemoved;");

        var grantDeletes = sql[sql.IndexOf("DELETE our", StringComparison.Ordinal)..sql.IndexOf("DELETE ou ", StringComparison.Ordinal)];
        grantDeletes.Should().NotContain("[IsActive]", "every state goes: an inactive row would come back on a rejoin")
            .And.NotContain("[ExpiresAt]", "an expired row would come back on a rejoin")
            .And.NotContain("[ApplicationId] =", "every application of the organization goes");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveMember_AnswersWhatTheBatchDeleted(bool membershipRemoved)
    {
        var applicationId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, rowFor: _ => new ResultSets(
            new { ApplicationId = applicationId, RoleId = roleId, RoleCode = "crm:agent", RoleName = "Agent \"lead\"" },
            new { ApplicationId = applicationId, PermissionId = permissionId, PermissionCode = "crm:tickets:read" },
            new { MembershipRemoved = membershipRemoved }));

        var removal = await new OrganizationRepository(db).RemoveMemberAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        removal.MembershipRemoved.Should().Be(membershipRemoved);
        removal.ApplicationRoles.Should().Equal(
            new Auth.Domain.ReadModels.Organizations.RemovedOrganizationApplicationRole(applicationId, roleId, "crm:agent", "Agent \"lead\""));
        removal.Permissions.Should().Equal(
            new Auth.Domain.ReadModels.Organizations.RemovedOrganizationPermission(applicationId, permissionId, "crm:tickets:read"));
    }

    // ---- V4: the read gate ------------------------------------------------------------

    /// <summary>The state predicates every organization-mediated role read must carry.</summary>
    private static readonly string[] LiveMembershipState =
    [
        "ou.[IsActive] = 1",
        "(ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())",
        "o.[IsActive] = 1",
        "oa.[IsActive] = 1",
        "(oa.[ExpiresAt] IS NULL OR oa.[ExpiresAt] > GETUTCDATE())",
    ];

    /// <summary>How the token read and the user page's read tie those rows to the role row.</summary>
    private static readonly string[] LiveMembershipKeys =
    [
        "INNER JOIN [dbo].[OrganizationUsers] ou",
        "ou.[OrganizationId] = our.[OrganizationId]",
        "ou.[UserId] = our.[UserId]",
        "INNER JOIN [dbo].[Organizations] o",
        "o.[Id] = our.[OrganizationId]",
        "INNER JOIN [dbo].[OrganizationApplications] oa",
        "oa.[OrganizationId] = our.[OrganizationId]",
        "oa.[ApplicationId] = our.[ApplicationId]",
    ];

    private static async Task<(string[] Token, string[] UserPage, string[] PermissionPairs)> OrganizationRoleReadsAsync()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        await new RoleRepository(db).GetUserRolesForApplicationAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await new OrganizationRepository(db).GetUserApplicationRolesAcrossOrganizationsAsync(Guid.NewGuid(), CancellationToken.None);
        await new OrganizationRepository(db).GetEffectivePermissionPairsForApplicationAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        db.Commands.Should().HaveCount(3);

        var token = Normalized(db.Commands[0].CommandText);
        var organizationBranch = token[(token.IndexOf(" UNION ", StringComparison.Ordinal) + " UNION ".Length)..];
        var userPage = Normalized(db.Commands[1].CommandText);
        var pairs = Normalized(db.Commands[2].CommandText);
        var pairsRoleBranch = pairs[..pairs.IndexOf(" UNION ", StringComparison.Ordinal)];

        return (
            Clauses(organizationBranch[organizationBranch.IndexOf("FROM ", StringComparison.Ordinal)..]),
            Clauses(userPage[userPage.IndexOf("FROM ", StringComparison.Ordinal)..userPage.IndexOf(" ORDER BY", StringComparison.Ordinal)]),
            Clauses(pairsRoleBranch[pairsRoleBranch.IndexOf("FROM ", StringComparison.Ordinal)..]));
    }

    [Fact]
    public async Task TheTokenRoleRead_TheUserPage_AndThePermissionPairs_CarryTheSameLiveMembershipPredicates()
    {
        var (token, userPage, permissionPairs) = await OrganizationRoleReadsAsync();

        token.Should().Contain(LiveMembershipState, "a removed member, a deactivated organization or a switched-off subscription keeps no role in the token");
        token.Should().Contain(LiveMembershipKeys, "the membership must be the person's, in the row's organization, for the row's application");
        userPage.Should().Contain(LiveMembershipState).And.Contain(LiveMembershipKeys);
        permissionPairs.Should().Contain(LiveMembershipState,
            "the permissions behind a role already required all of it; the role code must not ride where its permissions do not");
    }

    /// <summary>
    /// OI-225's equality, kept: the user page lists exactly what the application tokens carry
    /// through organizations, now on the statements the repositories send rather than their
    /// source text.
    /// </summary>
    [Fact]
    public async Task TheUserPage_ListsExactlyWhatTheTokenRoleReadCarries()
    {
        var (token, userPage, _) = await OrganizationRoleReadsAsync();

        // The two reads name their first table the other way round (FROM Roles JOIN rows, FROM
        // rows JOIN Roles); that pair is the same join either way. Every other join keeps its
        // keyword.
        static string SameJoin(string clause) =>
            clause is "INNER JOIN [dbo].[Roles] r" or "INNER JOIN [dbo].[OrganizationUserRoles] our"
                ? clause["INNER JOIN ".Length..]
                : clause;
        var tokenWithoutTheApplicationFilter = token
            .Where(clause => !clause.Contains("@ApplicationId", StringComparison.Ordinal))
            .Select(SameJoin);
        var userPageWithoutTheApplicationJoin = userPage
            .Where(clause => clause != "INNER JOIN [dbo].[Applications] a" && clause != "a.[Id] = our.[ApplicationId]")
            .Select(SameJoin);

        tokenWithoutTheApplicationFilter.Should().HaveCount(LiveMembershipKeys.Length + LiveMembershipState.Length + 7,
            "the roles and rows tables, their join, the live-membership joins, and the user, row, role and expiry predicates");
        userPageWithoutTheApplicationJoin.Should().BeEquivalentTo(tokenWithoutTheApplicationFilter);
    }

    // ---- V5: the in-organization grant check ------------------------------------------

    [Fact]
    public async Task TheGrantChecksRead_RequiresAnActiveUnexpiredMembership_InBothBranches()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);

        await new OrganizationRepository(db).GetEffectivePermissionCodesAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var branches = Normalized(db.Commands.Should().ContainSingle().Subject.CommandText).Split(" UNION ");
        branches.Should().HaveCount(2);
        foreach (var (branch, row) in branches.Zip(new[] { "our", "oup" }))
        {
            Clauses(branch[branch.IndexOf("FROM ", StringComparison.Ordinal)..]).Should().Contain(
            [
                "INNER JOIN [dbo].[OrganizationUsers] ou",
                $"ou.[OrganizationId] = {row}.[OrganizationId]",
                $"ou.[UserId] = {row}.[UserId]",
                "ou.[IsActive] = 1",
                "(ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > GETUTCDATE())",
            ], $"the {row} branch must read only a member's rows");
        }
    }

    // ---- V6: acceptance ----------------------------------------------------------------

    /// <summary>
    /// UpsertAppRoleSql's UPDATE, whole: every term of the assignment is written over the
    /// earlier row, which is found by the four columns of UQ_OrganizationUserRoles.
    /// </summary>
    private const string UpsertRestoresTheWholeRow =
        "UPDATE [dbo].[OrganizationUserRoles] WITH (UPDLOCK, HOLDLOCK) SET @RowId = [Id], "
        + "[IsActive] = @IsActive, [AssignedAt] = @AssignedAt, [AssignedBy] = @AssignedBy, "
        + "[ExpiresAt] = @ExpiresAt, [ModifiedAt] = @AssignedAt, [ModifiedBy] = @AssignedBy "
        + "WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId "
        + "AND [ApplicationId] = @ApplicationId AND [RoleId] = @RoleId;";

    [Fact]
    public async Task AcceptInvitation_WritesTheRoleThroughTheAssignmentsUpdateOrInsert_InsideItsTransaction()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var invitation = OrganizationInvitation.Create(organizationId, "invitee@example.com", Guid.NewGuid(), "hash", Guid.NewGuid());
        invitation.Accept(userId);

        var accepted = await new OrganizationRepository(db).AcceptInvitationAsync(
            invitation,
            OrganizationUser.Create(organizationId, userId, Guid.NewGuid(), invitation.InvitedBy),
            OrganizationUserRole.Create(organizationId, userId, Guid.NewGuid(), Guid.NewGuid(), invitation.InvitedBy),
            CancellationToken.None);

        accepted.Should().BeTrue();
        db.Commands.Should().HaveCount(3).And.OnlyContain(command => command.InTransaction);
        db.LastTransaction!.Committed.Should().BeTrue();

        var write = Normalized(db.Commands[2].CommandText);
        ContainsInOrder(write,
            "DECLARE @RowId UNIQUEIDENTIFIER;",
            // The whole restore, on the full key: a column left out keeps the earlier row's
            // value, so an expired row would be "Applied" and still expired.
            UpsertRestoresTheWholeRow,
            "IF @@ROWCOUNT = 0 BEGIN INSERT INTO [dbo].[OrganizationUserRoles]",
            "SET @RowId = @Id; END;");
        write.Should().NotContain("BEGIN TRANSACTION", "the acceptance's own transaction holds the locks; no nested transaction")
            .And.NotContain("ISOLATION LEVEL", "the acceptance keeps its isolation; the table hints hold the key range");
    }

    // ---- V8: the assignment's membership check ----------------------------------------

    [Fact]
    public async Task Assign_ChecksTheMembershipInsideItsSerializableStatement_BeforeTheWrite()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0, scalarFor: _ => null);

        var answered = await new OrganizationRepository(db).AssignAppRoleAsync(
            OrganizationUserRole.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        answered.Should().BeNull("no row was written: the answer is \"not a member\"");
        ContainsInOrder(Normalized(db.Commands.Should().ContainSingle().Subject.CommandText),
            "SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION;",
            "IF EXISTS ( SELECT 1 FROM [dbo].[OrganizationUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = @OrganizationId AND [UserId] = @UserId) BEGIN",
            UpsertRestoresTheWholeRow,
            "IF @@ROWCOUNT = 0 BEGIN INSERT INTO [dbo].[OrganizationUserRoles]",
            "END; END; COMMIT TRANSACTION; SELECT @RowId;");
    }
}
