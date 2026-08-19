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

    [Theory]
    [InlineData("ur.[ApplicationId] IS NULL")]
    [InlineData("r.[ApplicationId] IS NULL")]
    public void PlatformRoleQuery_ExcludesApplicationScopedRoles(string predicate)
    {
        PlatformRoleQuery().Should().Contain(
            predicate,
            "role codes become the token's roles claim, so an application's role would present that application's authority as the platform's");
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

    private static string PlatformRoleQuery() =>
        Slice(
            ReadPersistence("RoleRepository.cs"),
            "public async Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken)",
            "public async Task<IReadOnlyList<Role>> GetUserRolesForApplicationAsync(");

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
