namespace Auth_API.Tests.Configuration;

/// <summary>
/// Source-level guards over the invitation-token hashing change.
///
/// <para>
/// The invitation token was the one bearer credential in this system stored in
/// clear text while refresh tokens, authorization codes, password-reset tokens,
/// API keys and every OTP were hashed. A single SELECT on
/// <c>OrganizationInvitations</c> was a working invitation into the organization
/// the row named, with the role it named — a tenant boundary crossed by reading.
/// </para>
///
/// <para>
/// Three things have to stay true together for that to remain fixed, and none of
/// them is visible to a behavioural test: the entity must expose no property that
/// invites a caller to store a plaintext token, the repository must offer no
/// lookup that takes one, and the upgrade script that reconciles rows written
/// before the change must actually be included in the deployment. A script that
/// exists and is never run is the classic version of this bug, and it fails
/// silently — the publish log reads exactly the same.
/// </para>
/// </summary>
public class InvitationTokenHashingGuardTests
{
    private static string ReadSource(params string[] relativeParts)
        => File.ReadAllText(Path.Combine(SolutionDirectory(), Path.Combine(relativeParts)));

    [Fact]
    public void Entity_ExposesTokenHash_AndNoPlaintextTokenProperty()
    {
        var properties = typeof(Auth.Domain.Entities.OrganizationInvitation)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        properties.Should().Contain("TokenHash");
        properties.Should().NotContain("Token",
            "a property called Token on this entity is an invitation to store one");
    }

    [Fact]
    public void Repository_OffersNoLookupThatTakesAPlaintextToken()
    {
        var methods = typeof(Auth.Domain.Interfaces.Repositories.IOrganizationRepository)
            .GetMethods()
            .Select(m => m.Name)
            .ToList();

        methods.Should().Contain("GetInvitationByTokenHashAsync");
        methods.Should().NotContain("GetInvitationByTokenAsync",
            "leaving the old lookup in place lets a future call site hand it the plaintext " +
            "and silently reintroduce a clear-text comparison");
    }

    [Fact]
    public void UpgradeScript_IsIncludedInThePostDeploymentScript()
    {
        var postDeployment = ReadSource(
            "Auth_DB", "dbo", "PostDeployment", "Script.PostDeployment.sql");

        postDeployment.Should().Contain("2026-08-30_InvitationTokenHashing.sql",
            "an upgrade script that is never included runs nowhere, and the publish log " +
            "reads exactly the same as if it had");
    }

    [Fact]
    public void UpgradeScript_IsListedInTheDatabaseProject()
    {
        var project = ReadSource("Auth_DB", "Auth_DB.sqlproj");

        project.Should().Contain("2026-08-30_InvitationTokenHashing.sql",
            "a script missing from the project file is not carried into the DACPAC, so the " +
            "post-deployment :r include fails the build rather than the deploy");
    }

    [Fact]
    public void UpgradeScript_OnlyTouchesPendingRows()
    {
        var script = ReadSource(
            "Auth_DB", "dbo", "Scripts", "Upgrades", "2026-08-30_InvitationTokenHashing.sql");

        // Idempotence is the property that matters: this runs on EVERY publish.
        script.Should().Contain("WHERE [Status] = N'Pending'",
            "an accepted, declined or already-cancelled invitation must never be rewritten " +
            "by a script that runs on every deploy");

        // And it must be able to tell a hash from a pre-upgrade plaintext, or a
        // second publish would cancel invitations the new code just created.
        script.Should().Contain("LEN([Token])");
        script.Should().Contain("RIGHT([Token], 1)");
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the solution tree");
        return directory!.FullName;
    }
}
