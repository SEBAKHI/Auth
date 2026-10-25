using System.Text.RegularExpressions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Guards the boundary between application-scoped authority and platform authority.
///
/// <para>
/// A permission may be granted "to this user, but only inside application X"
/// (<c>UserPermissions.ApplicationId</c>), and a role may either belong to an
/// application (<c>Roles.ApplicationId</c>) or merely be assigned within one
/// (<c>UserRoles.ApplicationId</c>). The request contract, the console's
/// application selector and the columns themselves all present that grant as
/// confined to X.
/// </para>
///
/// <para>
/// The platform queries did not filter on any of those columns, so a grant
/// confined to X came back as plain platform authority and was written into the
/// platform token's <c>permissions</c> and <c>roles</c> claims — which
/// <c>PermissionRequirementHandler</c> reads unscoped. Worse,
/// <c>PermissionGrantGuard</c> reads the same query, so it agreed the holder
/// legitimately held the bled code and let them re-grant it with no application
/// scope at all, making the escalation permanent and independent of the
/// original row.
/// </para>
///
/// <para>
/// The boundary sits on the scope of the <b>grant</b> and of the
/// <b>assignment</b>, never on the scope of the referenced permission row:
/// handing someone the platform's own <c>users:*</c> "but only inside CRM" is
/// exactly the case that must be excluded, and there the permission row is
/// platform-owned while the grant is not.
/// </para>
///
/// <para>
/// The repositories are Dapper + raw SQL and the test project has no database,
/// so the SQL text itself is the unit under test. The behaviour was verified
/// against a live database separately: with an application-scoped
/// <c>users:*</c> row present, the old query returned it and the new one
/// returns nothing, while the application-scoped overload still returns it.
/// </para>
/// </summary>
public class PlatformAuthorityScopeSqlTests
{
    [Theory]
    [InlineData("up.[ApplicationId] IS NULL", "a direct grant confined to one application")]
    [InlineData("ur.[ApplicationId] IS NULL", "a role assignment confined to one application")]
    [InlineData("r.[ApplicationId] IS NULL", "a role owned by one application")]
    public void PlatformPermissionQuery_ExcludesApplicationScopedAuthority(
        string predicate,
        string whatItExcludes)
    {
        PlatformPermissionQuery().Should().Contain(
            predicate,
            $"{whatItExcludes} must never reach the platform token or PermissionGrantGuard");
    }

    [Fact]
    public void PlatformPermissionQuery_TakesNoApplicationParameter()
    {
        // A platform query that accepts an application id is no longer a
        // platform query; the scope must be pinned to NULL, not supplied.
        PlatformPermissionQuery().Should().NotContain(
            "@ApplicationId",
            "the platform overload resolves platform authority only, so there is no application to parameterise");
    }

    /// <summary>
    /// X12 PR B: the platform query for every account at once — who holds platform
    /// authority — carries the same boundary as the per-account one, so an
    /// application-scoped grant never makes anyone an administrator who is told
    /// about, or chosen to act on, another account.
    /// </summary>
    [Theory]
    [InlineData("up.[ApplicationId] IS NULL", "a direct grant confined to one application")]
    [InlineData("ur.[ApplicationId] IS NULL", "a role assignment confined to one application")]
    [InlineData("r.[ApplicationId] IS NULL", "a role owned by one application")]
    [InlineData("ur.[IsActive] = 1", "an inactive assignment")]
    [InlineData("r.[IsActive] = 1", "an inactive role")]
    [InlineData("up.[IsActive] = 1", "an inactive direct grant")]
    [InlineData("(ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())", "an expired assignment")]
    [InlineData("(up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE())", "an expired direct grant")]
    public void PlatformHoldersQuery_KeepsThePlatformBoundary(string predicate, string whatItExcludes)
    {
        var query = PlatformHoldersQuery();

        query.Should().Contain(predicate, $"{whatItExcludes} is not platform authority");
        query.Should().NotContain("@ApplicationId", "a platform query has no application to parameterise");
    }

    /// <remarks>
    /// The fragments above can all be present while an <c>OR</c> undoes one of them;
    /// each branch's whole WHERE clause is pinned here, whitespace normalised, both
    /// <c>p.[IsActive] = 1</c> included. Deliberate break:
    /// <c>AND (ur.[ApplicationId] IS NULL OR 1 = 1)</c>.
    /// </remarks>
    [Fact]
    public void PlatformHoldersQuery_EachBranchsWholeWhereClause_IsThePlatformBoundary()
    {
        var query = Regex.Replace(PlatformHoldersQuery(), @"\s+", " ");

        var clauses = Regex.Matches(query, @"WHERE (?<clause>.+?) (?:UNION|\) SELECT)")
            .Select(match => match.Groups["clause"].Value)
            .ToList();

        clauses.Should().Equal(
            "ur.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL AND ur.[IsActive] = 1 AND r.[IsActive] = 1"
                + " AND p.[IsActive] = 1 AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())",
            "up.[ApplicationId] IS NULL AND up.[IsActive] = 1 AND p.[IsActive] = 1"
                + " AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE())");
    }

    /// <summary>
    /// OI-224: the page of accounts a user list shows, in one read, is the platform
    /// overload for many ids. Each branch's whole WHERE clause is the per-account
    /// boundary plus the page's ids, and nothing more. Deliberate break:
    /// <c>AND (up.[ApplicationId] IS NULL OR 1 = 1)</c>.
    /// </summary>
    [Fact]
    public void PlatformPageQuery_EachBranchsWholeWhereClause_IsThePlatformBoundary()
    {
        var query = Regex.Replace(PlatformPageQuery(), @"\s+", " ");

        var clauses = Regex.Matches(query, @"WHERE (?<clause>.+?) (?:UNION|ORDER BY)")
            .Select(match => match.Groups["clause"].Value)
            .ToList();

        clauses.Should().Equal(
            "ur.[UserId] IN @UserIds AND ur.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL AND ur.[IsActive] = 1"
                + " AND r.[IsActive] = 1 AND p.[IsActive] = 1 AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())",
            "up.[UserId] IN @UserIds AND up.[ApplicationId] IS NULL AND up.[IsActive] = 1 AND p.[IsActive] = 1"
                + " AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE())");
        query.Should().NotContain("@ApplicationId", "a platform query has no application to parameterise");
    }

    /// <summary>
    /// OI-224: who counts as a full administrator — the platform boundary narrowed to
    /// the wildcard. A <c>*</c> held only inside one application makes nobody a full
    /// administrator, so it must not keep the last one's action from being refused.
    /// Deliberate break: drop <c>ur.[ApplicationId] IS NULL</c>.
    /// </summary>
    [Fact]
    public void FullAdministratorQuery_EachBranchsWholeWhereClause_IsThePlatformBoundaryOnTheWildcard()
    {
        var query = Regex.Replace(FullAdministratorQuery(), @"\s+", " ");

        // The branches follow the select list, whose EXISTS (the account's linked
        // external sign-ins) has a WHERE of its own that is not a grant predicate.
        var branches = query[query.IndexOf("FROM ( SELECT", StringComparison.Ordinal)..];
        var clauses = Regex.Matches(branches, @"WHERE (?<clause>.+?) (?:UNION ALL|\) g)")
            .Select(match => match.Groups["clause"].Value)
            .ToList();

        clauses.Should().Equal(
            "p.[Code] = N'*' AND ur.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL AND ur.[IsActive] = 1"
                + " AND r.[IsActive] = 1 AND p.[IsActive] = 1 AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())",
            "p.[Code] = N'*' AND up.[ApplicationId] IS NULL AND up.[IsActive] = 1 AND p.[IsActive] = 1"
                + " AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE())");
        query.Should().Contain("u.[Status], u.[FailedLoginAttempts] AS [FailedAttempts], u.[LockoutEndUtc]",
            "whether each holder can sign in is decided from these columns by the sign-in path's own checks");
        query.Should().Contain("u.[IsEmailConfirmed] AS [EmailConfirmed], u.[IsDeleted], g.[RoleId]");

        // OI-226 T5 and T6: columns, never predicates — the caller decides that a grant
        // with an end date, or an account with no way to authenticate, does not count.
        query.Should().Contain("g.[RoleId], g.[ExpiresAt]")
            .And.Contain("SELECT ur.[UserId], ur.[RoleId], ur.[ExpiresAt]")
            .And.Contain("SELECT up.[UserId], NULL, up.[ExpiresAt]")
            .And.Contain("CAST(CASE WHEN u.[PasswordHash] IS NULL THEN 0 ELSE 1 END AS BIT) AS [HasPassword]")
            .And.Contain("SELECT 1 FROM [dbo].[UserExternalLogins] el WHERE el.[UserId] = u.[Id] ) THEN 1 ELSE 0 END AS BIT) AS [HasExternalLogin]");
    }

    [Theory]
    [InlineData("ur.[ApplicationId] IS NULL")]
    [InlineData("r.[ApplicationId] IS NULL")]
    public void PlatformRoleQuery_ExcludesApplicationScopedRoles(string predicate)
    {
        PlatformRoleQuery().Should().Contain(
            predicate,
            "role codes become the token's roles claim, so an application's role would present that application's authority as the platform's");
    }

    /// <summary>
    /// OI-225 (6): the user list's roles for a page of accounts are the one-account
    /// read's roles, by the same predicates: only the account filter differs. Deliberate
    /// break: drop or change one predicate in either query.
    /// </summary>
    [Fact]
    public void PlatformPageRoleQuery_HasTheOneAccountQuerysPredicates()
    {
        static string Where(string query) =>
            Regex.Replace(
                Regex.Match(query, @"WHERE (?<clause>.+?)\s+ORDER BY", RegexOptions.Singleline).Groups["clause"].Value,
                @"\s+", " ");

        var single = Where(PlatformRoleQuery());
        var page = Where(PlatformPageRoleQuery());

        single.Should().StartWith("ur.[UserId] = @UserId AND ");
        page.Should().Be(single.Replace("ur.[UserId] = @UserId", "ur.[UserId] IN @UserIds"),
            "the page read must return, per account, exactly what the one-account read returns");
    }

    [Theory]
    [InlineData("up.[ApplicationId] = @ApplicationId")]
    [InlineData("r.[ApplicationId] = @ApplicationId")]
    public void ApplicationScopedQuery_KeepsItsOwnBoundary(string predicate)
    {
        // The twin of the platform query, enforcing the same boundary from the
        // other side: platform authority (a NULL scope) must not ride into a
        // partner application's token either. Pinned here so the two cannot
        // drift apart when one of them is edited.
        ApplicationScopedPermissionQuery().Should().Contain(predicate);
    }

    private static string PlatformPermissionQuery() =>
        Slice(
            ReadPersistence("PermissionRepository.cs"),
            "public async Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(\r\n        Guid userId,\r\n        CancellationToken cancellationToken)",
            "public async Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(\r\n        Guid userId,\r\n        Guid applicationId,");

    private static string ApplicationScopedPermissionQuery() =>
        Slice(
            ReadPersistence("PermissionRepository.cs"),
            "public async Task<IReadOnlyList<string>> GetUserEffectivePermissionsAsync(\r\n        Guid userId,\r\n        Guid applicationId,",
            "public async Task<bool> UserHasPermissionAsync(");

    private static string PlatformHoldersQuery() =>
        Slice(
            ReadPersistence("PermissionRepository.cs"),
            "public async Task<IReadOnlyList<PlatformAuthorityHolder>> GetUsersHoldingPlatformPermissionsAsync(",
            "public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetUsersEffectivePermissionsAsync(");

    private static string PlatformPageQuery() =>
        Slice(
            ReadPersistence("PermissionRepository.cs"),
            "public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetUsersEffectivePermissionsAsync(",
            "internal const string FullAdministratorGrantsSql = @\"");

    private static string FullAdministratorQuery() =>
        Slice(
            ReadPersistence("PermissionRepository.cs"),
            "internal const string FullAdministratorGrantsSql = @\"",
            "public async Task<IReadOnlyList<FullAdministratorGrant>> GetUsersHoldingWildcardPermissionAsync(");

    private static string PlatformRoleQuery() =>
        Slice(
            ReadPersistence("RoleRepository.cs"),
            "public async Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)",
            "public async Task<IReadOnlyList<Role>> GetUserRolesForApplicationAsync(");

    private static string PlatformPageRoleQuery() =>
        Slice(
            ReadPersistence("RoleRepository.cs"),
            "public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Role>>> GetUserRolesForUsersAsync(",
            "public async Task<bool> ExistsByCodeAsync(");

    /// <summary>
    /// Takes the text of exactly one method, so a predicate present in a
    /// neighbouring overload cannot satisfy an assertion about this one.
    /// </summary>
    private static string Slice(string source, string startMarker, string endMarker)
    {
        var normalised = source.Replace("\r\n", "\n");
        var start = normalised.IndexOf(startMarker.Replace("\r\n", "\n"), StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0,
            $"the method beginning '{Head(startMarker)}' must exist; if it was renamed, this guard needs updating rather than deleting");

        var end = normalised.IndexOf(endMarker.Replace("\r\n", "\n"), start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start,
            $"the method beginning '{Head(endMarker)}' must follow it");

        return normalised[start..end];
    }

    private static string Head(string marker)
    {
        var firstLine = marker.Split('\n')[0];
        return firstLine.Length > 60 ? firstLine[..60] : firstLine;
    }

    private static string ReadPersistence(string fileName) =>
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
