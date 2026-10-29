using System.Text.RegularExpressions;

namespace Auth_API.Tests.Authentication.ClientCredentials;

/// <summary>
/// Where the machine scheme may be named (OI-128). A closed list of exactly two controller files,
/// each naming it once at class level: the machine directory (OI-202), and the machine credential
/// checks (OI-128 PR B: key validation, webhook-key validation, introspection). A stray
/// <c>[Authorize(AuthenticationSchemes = MachineScheme)]</c> anywhere else would be exactly the
/// unreviewed widening this guard exists to catch.
/// </summary>
public class MachineSchemePlacementGuardTests
{
    private const string DirectoryControllerFile = "ApplicationMachineOrganizationsController.cs";
    private const string ChecksControllerFile = "MachineCredentialChecksController.cs";

    [Fact]
    public void TheSchemeLiteral_AppearsOnlyInTheBuilder()
    {
        FilesContaining("\"MachineClient\"").Should().Equal("AccessTokenValidation.cs");
    }

    [Fact]
    public void TheSchemeConstant_IsUsedOnlyByTheBuilderTheRegistrationAndTheTwoMachineControllers()
    {
        FilesContaining("MachineScheme").Should().BeEquivalentTo(
            "AccessTokenValidation.cs", "BearerSchemeRegistration.cs", DirectoryControllerFile, ChecksControllerFile);
    }

    [Fact]
    public void TheMachineCredentialChecks_NameTheSchemeOnce_AtClassLevel_AndNoOtherGuard()
    {
        var controller = Source(ChecksControllerFile);

        Regex.Matches(controller, @"\bMachineScheme\b").Should().ContainSingle(
            "one [Authorize] on the machine credential checks and nowhere else in it");
        Regex.IsMatch(
                controller,
                @"\[Authorize\(AuthenticationSchemes = AccessTokenValidation\.MachineScheme\)\]\s*\n(\[[^\n]*\]\s*\n)*public sealed class MachineCredentialChecksController\b")
            .Should().BeTrue("the scheme is named at class level, so every action has it and none names another");

        // A machine token carries no permissions, and no user stands behind it to hold any.
        foreach (var forbidden in new[]
                 {
                     "[RequirePermission", "RequirePermission(", "PermissionRequirement", "[AllowAnonymous",
                     "PlatformScope", "UserInfoScheme", "JwtBearerDefaults", "HasPermissionClaim",
                     "JwtClaimNames.Permissions", "JwtClaimNames.OrgPermissions", "GetCurrentUserId",
                 })
        {
            controller.Should().NotContain(forbidden);
        }

        var type = typeof(Auth_API.Modules.MachineCredentialChecks.Controllers.MachineCredentialChecksController);
        type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Should().ContainSingle()
            .Which.AuthenticationSchemes.Should().Be(Auth_API.Common.Authentication.AccessTokenValidation.MachineScheme);
        type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.IAllowAnonymous), inherit: true)
            .Should().BeEmpty();

        // Each check requires its own machine scope, and that scope is what its budget is read by.
        var actions = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        actions.Should().HaveCount(3, "the three checks, and nothing else is exposed")
            .And.OnlyContain(method =>
                !method.GetCustomAttributes(inherit: true).Any(attribute =>
                    attribute is Microsoft.AspNetCore.Authorization.IAuthorizeData
                    || attribute is Microsoft.AspNetCore.Authorization.IAllowAnonymous));
        var scopes = actions
            .Select(method => method.GetCustomAttributes(typeof(Auth_API.Common.Middleware.MachineCredentialCheckAttribute), inherit: false)
                .Cast<Auth_API.Common.Middleware.MachineCredentialCheckAttribute>()
                .Single().Profile.RequiredScope);
        scopes.Should().BeEquivalentTo(Auth.Domain.Constants.MachineClients.Scopes.All);
    }

    [Fact]
    public void TheMachineDirectory_NamesTheSchemeOnce_AtClassLevel_AndNoOtherGuard()
    {
        var controller = Source(DirectoryControllerFile);

        Regex.Matches(controller, @"\bMachineScheme\b").Should().ContainSingle(
            "one [Authorize] on the machine directory controller and nowhere else in it");
        Regex.IsMatch(
                controller,
                @"\[Authorize\(AuthenticationSchemes = AccessTokenValidation\.MachineScheme\)\]\s*\n(\[[^\n]*\]\s*\n)*public sealed class ApplicationMachineOrganizationsController\b")
            .Should().BeTrue("the scheme is named at class level, so every action has it and none names another");

        // A permission attribute would evaluate a token claim instead of the database; a machine
        // token carries no permissions, and no user stands behind it to hold any.
        foreach (var forbidden in new[]
                 {
                     "[RequirePermission", "RequirePermission(", "PermissionRequirement", "[AllowAnonymous",
                     "PlatformScope", "UserInfoScheme", "JwtBearerDefaults", "HasPermissionClaim",
                     "JwtClaimNames.Permissions", "JwtClaimNames.OrgPermissions",
                 })
        {
            controller.Should().NotContain(forbidden);
        }

        var type = typeof(Auth_API.Modules.OrganizationManagement.Controllers.ApplicationMachineOrganizationsController);
        type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Should().ContainSingle()
            .Which.AuthenticationSchemes.Should().Be(Auth_API.Common.Authentication.AccessTokenValidation.MachineScheme);
        type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.IAllowAnonymous), inherit: true)
            .Should().BeEmpty();
        type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Should().HaveCount(3, "the three reads, and nothing else is exposed")
            .And.OnlyContain(method =>
                !method.GetCustomAttributes(inherit: true).Any(attribute =>
                    attribute is Microsoft.AspNetCore.Authorization.IAuthorizeData
                    || attribute is Microsoft.AspNetCore.Authorization.IAllowAnonymous));
    }

    [Fact]
    public void TheSharedDirectoryBase_NamesNoSchemeAndNoGuard()
    {
        // The base both directory doors derive from holds the answer and the binder only: a
        // scheme or a guard there would reach both doors at once.
        var shared = Source("ApplicationDirectoryController.cs");

        foreach (var forbidden in new[]
                 {
                     "[Authorize", "AuthenticationSchemes", "MachineScheme", "UserInfoScheme", "[RequirePermission",
                     "[AllowAnonymous", "PlatformScope",
                 })
        {
            shared.Should().NotContain(forbidden);
        }
    }

    private static string Source(string fileName)
    {
        var matches = SourceFiles().Where(file => Path.GetFileName(file) == fileName).ToList();
        matches.Should().ContainSingle("{0} must exist exactly once in Auth_API", fileName);
        return File.ReadAllText(matches[0]).Replace("\r\n", "\n");
    }

    private static List<string> FilesContaining(string text) =>
        SourceFiles()
            .Where(file => File.ReadAllText(file).Contains(text, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

    private static List<string> SourceFiles()
    {
        var root = Path.Combine(SolutionDirectory(), "Auth_API");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        files.Should().HaveCountGreaterThan(50, "the scan must see the whole API");
        return files;
    }

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
