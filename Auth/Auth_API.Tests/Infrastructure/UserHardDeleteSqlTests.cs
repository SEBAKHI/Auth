using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the user hard-delete purge against schema drift.
///
/// Users is referenced by ~20 non-cascading foreign keys plus loose (FK-less)
/// user references in AuditLogs, NotificationOutbox and RevokedTokens. The
/// purge in <c>UserRepository.HardDeleteAsync</c> must account for every one
/// of them: rows the user owns are deleted, actor references on records that
/// belong to other entities are reattributed, and Organizations ownership is
/// resolved at the application layer before the purge runs. These tests parse
/// the DACPAC schema for the authoritative FK list, so adding a new
/// Users-referencing table without extending the purge fails the build.
///
/// The repositories are Dapper + raw SQL and the test project has no database,
/// so the SQL text itself is the unit under test.
/// </summary>
public class UserHardDeleteSqlTests
{
    /// <summary>
    /// Organizations.OwnerId is intentionally not part of the purge SQL: the
    /// command handler resolves owned organizations first (blocking when other
    /// members depend on one, deleting sole-member ones), and the FK turns any
    /// gap into a 409 instead of an orphan.
    /// </summary>
    private static readonly HashSet<(string Table, string Column)> HandledOutsideThePurge =
    [
        ("Organizations", "OwnerId"),
    ];

    [Fact]
    public void EveryForeignKeyReferencingUsers_IsCoveredByThePurge()
    {
        var purgeSql = HardDeleteSql();
        var uncovered = new List<string>();

        foreach (var (table, column) in ForeignKeysReferencingUsers())
        {
            if (HandledOutsideThePurge.Contains((table, column)))
            {
                continue;
            }

            var deleted = new Regex(
                $@"DELETE\s+FROM\s+\[dbo\]\.\[{table}\][^;]*\[{column}\]\s*=\s*@Id",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var reattributed = new Regex(
                $@"UPDATE\s+\[dbo\]\.\[{table}\]\s+SET\s+\[{column}\]",
                RegexOptions.IgnoreCase);

            if (!deleted.IsMatch(purgeSql) && !reattributed.IsMatch(purgeSql))
            {
                uncovered.Add($"{table}.{column}");
            }
        }

        uncovered.Should().BeEmpty(
            "every foreign key referencing Users must be deleted or reattributed by " +
            "HardDeleteAsync, otherwise the purge dies on SQL error 547 — extend the " +
            "purge SQL (or the handler) for these references");
    }

    [Fact]
    public void OrganizationsOwnership_IsResolvedByTheSharedGuard()
    {
        var guard = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Application", "Features", "Users",
            "Common", "OwnedOrganizationDeletionGuard.cs"));

        guard.Should().Contain("GetByOwnerAsync",
            "the shared guard must resolve owned organizations before any purge; " +
            "Organizations.OwnerId is exempted from the purge SQL on that basis");
        guard.Should().Contain("GetMemberCountsAsync",
            "owned organizations with other members must block the purge");

        foreach (var handlerPath in new[]
        {
            Path.Combine("DeleteUser", "DeleteUserCommandHandler.cs"),
            Path.Combine("HardDeleteUser", "HardDeleteUserCommandHandler.cs")
        })
        {
            var handler = File.ReadAllText(Path.Combine(
                SolutionDirectory(), "Auth.Application", "Features", "Users", handlerPath));
            handler.Should().Contain("OwnedOrganizationDeletionGuard",
                $"{handlerPath} must apply the owned-organization rule through the shared guard");
        }
    }

    [Theory]
    [InlineData(@"UPDATE\s+\[dbo\]\.\[AuditLogs\]\s+SET\s+\[UserId\]\s*=\s*NULL,\s*\[OldValues\]\s*=\s*NULL,\s*\[NewValues\]\s*=\s*NULL,\s*\[Details\]\s*=\s*NULL,\s*\[IpAddress\]\s*=\s*NULL,\s*\[UserAgent\]\s*=\s*NULL\s+WHERE\s+\[UserId\]\s*=\s*@Id",
        "audit rows about the user must be anonymized in place (identity and PII payloads stripped), never deleted")]
    [InlineData(@"UPDATE\s+\[dbo\]\.\[AuditLogs\]\s+SET\s+\[PerformedBy\]\s*=\s*@SystemUserId,\s*\[IpAddress\]\s*=\s*NULL,\s*\[UserAgent\]\s*=\s*NULL\s+WHERE\s+\[PerformedBy\]\s*=\s*@Id",
        "audit rows the user performed must be reattributed to the system account with their IP/agent stripped")]
    [InlineData(@"UPDATE\s+\[dbo\]\.\[LoginAttempts\]\s+SET\s+\[UserId\]\s*=\s*NULL,\s*\[Username\]\s*=\s*N'\[deleted\]',\s*\[IpAddress\]\s*=\s*N'0\.0\.0\.0',\s*\[UserAgent\]\s*=\s*NULL\s+WHERE\s+\[UserId\]\s*=\s*@Id\s+OR\s+\[Username\]\s*=\s*@Email\s+OR\s+\[Username\]\s*=\s*@Username",
        "login attempts must be anonymized (fraud signal retained within retention, identity stripped) — " +
        "including IpAddress and UserAgent, which the published policy promises are de-identified " +
        "immediately and which a UserId/Username-only update left intact for a further 365 days")]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[NotificationOutbox\]\s+WHERE\s+\[RecipientUserId\]\s*=\s*@Id\s+OR\s+\[Recipient\]\s*=\s*@Email",
        "queued notifications addressed to the user must be purged")]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[RevokedTokens\]\s+WHERE\s+\[RevocationKey\]",
        "denylist entries keyed by the user id must be purged")]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[AccountDeletionVerifications\]\s+WHERE\s+\[UserId\]\s*=\s*@Id",
        "deletion re-auth OTP rows are Class A and must be purged with the account")]
    public void LooseUserReferences_AreCoveredByThePurge(string pattern, string because)
    {
        new Regex(pattern, RegexOptions.IgnoreCase).IsMatch(HardDeleteSql())
            .Should().BeTrue(because);
    }

    /// <summary>
    /// Rows bound to the account by IDENTIFIER rather than by user id. They have
    /// no foreign key, so the schema-drift guard above cannot see them: each one
    /// is asserted explicitly here.
    ///
    /// This class is the reason a finite identifier-reservation window is unsafe
    /// without them. While a deleted address can never be re-registered, an
    /// untouched row is "only" surviving PII; the moment the reservation is
    /// bounded, the next holder of that address inherits it — a pending
    /// organization invitation binds on the e-mail string, not on a user id, so
    /// inheriting one is a membership grant, not a privacy leak.
    /// </summary>
    [Theory]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[OrganizationInvitations\]\s+WHERE\s+\[InvitedBy\]\s*=\s*@Id\s+OR\s+\[Email\]\s*=\s*@Email",
        "invitations ADDRESSED TO the destroyed identifier must be purged, not only those the user sent: " +
        "RegisterWithInvitation creates the account from invitation.Email, so a surviving Pending row is an " +
        "organization membership waiting for whoever next holds that address")]
    [InlineData(@"DELETE\s+FROM\s+\[dbo\]\.\[NotificationOutbox\]\s+WHERE\s+\[RecipientUserId\]\s*=\s*@Id\s+OR\s+\[Recipient\]\s*=\s*@Email",
        "outbox rows queued before the account existed carry a NULL RecipientUserId, so the address must be " +
        "matched too — Recipient, Subject and BodyHtml hold rendered personal data")]
    [InlineData(@"\[Username\]\s*=\s*@Email\s+OR\s+\[Username\]\s*=\s*@Username",
        "LoginAttempts.Username holds the ATTEMPTED identifier: attempts that never resolved to an account " +
        "keep the e-mail in clear text under a NULL UserId and are missed by a UserId-only predicate")]
    public void IdentifierBoundRows_AreCoveredByThePurge(string pattern, string because)
    {
        new Regex(pattern, RegexOptions.IgnoreCase).IsMatch(HardDeleteSql())
            .Should().BeTrue(because);
    }

    [Fact]
    public void Purge_PassesTheIdentifiersItMatchesOn()
    {
        var purgeSql = HardDeleteSql();

        purgeSql.Should().Contain("@Email",
            "the identifier-bound predicates need the account's own e-mail as a parameter");
        purgeSql.Should().Contain("@Username",
            "the LoginAttempts predicate needs the account's own username as a parameter");

        new Regex(@"Email\s*=\s*identifiers\.NormalizedEmail\.ToLowerInvariant\(\)")
            .IsMatch(purgeSql).Should().BeTrue(
                "the e-mail parameter must be the lower-invariant form, which is what Email.Value stores in " +
                "OrganizationInvitations.Email and NotificationOutbox.Recipient — passing NormalizedEmail " +
                "(upper-invariant) would silently match nothing under a case-sensitive collation");
    }

    [Fact]
    public void Purge_WritesTheTombstoneWithTheReservationDigestAndKeyVersion()
    {
        var purgeSql = HardDeleteSql();

        new Regex(@"MERGE\s+\[dbo\]\.\[AccountDeletionTombstones\]", RegexOptions.IgnoreCase)
            .IsMatch(purgeSql).Should().BeTrue(
                "destruction must write the tombstone idempotently before anything is removed");
        purgeSql.Should().ContainAll("@EmailHash", "@PolicyVersion", "@KeyVersion");

        purgeSql.Should().NotContain("@UsernameHash",
            "the username reservation was removed: nothing ever read it, the username is derived rather " +
            "than chosen by the user, and writing it stored a second identifier of a person for a check " +
            "that did not exist");
    }

    [Fact]
    public void Tombstone_CarriesAKeyVersion_SoTheIdentifierKeyCanEverBeRotated()
    {
        var ddl = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth_DB", "dbo", "Tables", "Security", "AccountDeletionTombstones.sql"));

        ddl.Should().Contain("[KeyVersion]",
            "without a key version, digests written under an old key are indistinguishable from current " +
            "ones, so the permanent-by-necessity key could never be rotated");
        ddl.Should().NotContain("[UsernameHash]",
            "the column and its index were removed along with the unread username reservation");
        ddl.Should().Contain("IX_AccountDeletionTombstones_DeletedAtUtc",
            "the retention sweep deletes by DeletedAtUtc and needs an index to do it");
    }

    [Fact]
    public void Purge_NeverDeletesLogsOrDestructionEvidence()
    {
        var purgeSql = HardDeleteSql();

        new Regex(@"DELETE\s+FROM\s+\[dbo\]\.\[(AuditLogs|LoginAttempts|AccountDeletionRequests|AccountDeletionTombstones)\]",
                RegexOptions.IgnoreCase)
            .IsMatch(purgeSql).Should().BeFalse(
                "the audit/login history is anonymized (Class B/C), and deletion requests and tombstones " +
                "are destruction evidence. Their disposal belongs to the retention sweep on a declared " +
                "schedule, never to the purge — a tombstone deleted here would release the identifier at " +
                "the very moment it is supposed to start being reserved");
    }

    [Fact]
    public void UsersRow_IsDeletedLast_AndOnlyWhenStillSoftDeleted()
    {
        var purgeSql = HardDeleteSql();

        var finalDelete = new Regex(
            @"DELETE\s+FROM\s+\[dbo\]\.\[Users\]\s+WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[IsDeleted\]\s*=\s*1",
            RegexOptions.IgnoreCase);
        finalDelete.IsMatch(purgeSql).Should().BeTrue(
            "the account row must only be removed while still flagged deleted — " +
            "the predicate is the last in-database guard against purging a live account");

        var match = finalDelete.Match(purgeSql);
        purgeSql[(match.Index + match.Length)..].Should().NotContain("DELETE FROM",
            "the Users row must be the final delete so child rows never outlive it");
    }

    [Fact]
    public void Purge_VerifiesEligibilityUnderAnUpdateLock()
    {
        var repository = ReadPersistenceFile("UserRepository.cs");

        new Regex(@"WITH\s+\(UPDLOCK,\s*HOLDLOCK\)\s+WHERE\s+\[Id\]\s*=\s*@Id\s+AND\s+\[IsDeleted\]\s*=\s*1",
                RegexOptions.IgnoreCase)
            .IsMatch(repository).Should().BeTrue(
                "HardDeleteAsync must re-verify the soft-deleted flag inside the " +
                "transaction under an update lock, so a concurrent write cannot race " +
                "a live account into the purge");
    }

    /// <summary>
    /// Parses every table definition in the DACPAC project for foreign keys
    /// that reference [dbo].[Users], returning (table, referencing column).
    /// </summary>
    private static IReadOnlyList<(string Table, string Column)> ForeignKeysReferencingUsers()
    {
        var tablesDirectory = Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Tables");
        var foreignKey = new Regex(
            @"FOREIGN\s+KEY\s*\(\[(?<column>\w+)\]\)\s*REFERENCES\s+\[dbo\]\.\[Users\]",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var references = new List<(string, string)>();
        foreach (var file in Directory.EnumerateFiles(tablesDirectory, "*.sql", SearchOption.AllDirectories))
        {
            var table = Path.GetFileNameWithoutExtension(file);
            foreach (System.Text.RegularExpressions.Match match in foreignKey.Matches(File.ReadAllText(file)))
            {
                if (!string.Equals(table, "Users", StringComparison.OrdinalIgnoreCase))
                {
                    references.Add((table, match.Groups["column"].Value));
                }
            }
        }

        references.Should().NotBeEmpty("the schema parse found no Users foreign keys — the pattern is broken");
        return references;
    }

    /// <summary>Extracts the HardDeleteAsync method body from the repository source.</summary>
    private static string HardDeleteSql()
    {
        var repository = ReadPersistenceFile("UserRepository.cs");

        var start = repository.IndexOf("HardDeleteAsync", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "UserRepository must implement HardDeleteAsync");

        var end = repository.IndexOf("transaction.Commit", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "HardDeleteAsync must commit its transaction");

        return repository[start..end];
    }

    private static string ReadPersistenceFile(string fileName) =>
        File.ReadAllText(Path.Combine(
            SolutionDirectory(), "Auth.Infrastructure", "Persistence", fileName));

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
