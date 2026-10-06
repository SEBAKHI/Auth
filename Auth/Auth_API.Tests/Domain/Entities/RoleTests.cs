using System.Globalization;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth_API.Tests.Helpers;
using ErrorOr;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// Unit tests for the <see cref="Role"/> entity: the case a new code is stored in
/// (OI-73), and which permissions a role may hold (OI-74).
/// </summary>
public class RoleTests
{
    [Theory]
    [InlineData("Institution_Manager", "institution_manager")]
    [InlineData("ADMIN", "admin")]
    [InlineData("Support-Agent", "support-agent")]
    [InlineData("support-agent", "support-agent")]
    public void Create_CodeInAnyCase_StoresItLowercase(string code, string expected)
    {
        var role = Role.Create(Guid.NewGuid(), code, "Name", null, Guid.NewGuid());

        role.Code.Should().Be(expected);
    }

    /// <summary>
    /// Turkish is one of the seven languages, so the request culture can be tr-TR,
    /// where culture-sensitive lowercasing turns "I" into a dotless "ı".
    /// </summary>
    [Theory]
    [InlineData("Institution_Manager", "institution_manager")]
    [InlineData("ADMIN", "admin")]
    public void Create_UnderTurkishCulture_StoresTheInvariantLowercase(string code, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            // Floor: without a culture whose lowercasing differs from the
            // invariant one, this test could not fail.
            "I".ToLower(CultureInfo.CurrentCulture).Should().Be("ı");

            var role = Role.Create(null, code, "Name", null, Guid.NewGuid());

            role.Code.Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EnsureCanHold_PermissionOfTheSameApplication_Succeeds()
    {
        var applicationId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(applicationId: applicationId);
        var permission = TestHelpers.CreatePermission(applicationId: applicationId, code: "edis:fairs:view");

        role.EnsureCanHold(permission).IsError.Should().BeFalse();
    }

    [Fact]
    public void EnsureCanHold_PlatformRoleAndPlatformPermission_Succeeds()
    {
        var role = TestHelpers.CreateRole(applicationId: null);
        var permission = TestHelpers.CreatePermission(applicationId: null, code: "*", level: 0, isWildcard: true);

        role.EnsureCanHold(permission).IsError.Should().BeFalse();
    }

    private static readonly Guid Edis = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Crm = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static TheoryData<Guid?, Guid?, string> MismatchedScopes() => new()
    {
        // An application role and a platform permission, the wildcard included.
        { Edis, null, "*" },
        { Edis, null, "users:read" },
        // An application role and another application's permission.
        { Edis, Crm, "crm:leads:read" },
        // A platform role and an application's permission.
        { null, Edis, "edis:fairs:view" },
    };

    [Theory]
    [MemberData(nameof(MismatchedScopes))]
    public void EnsureCanHold_PermissionOfAnotherScope_ReturnsPermissionNotForApplication(
        Guid? roleApplicationId, Guid? permissionApplicationId, string permissionCode)
    {
        var role = TestHelpers.CreateRole(applicationId: roleApplicationId);
        var permission = TestHelpers.CreatePermission(
            applicationId: permissionApplicationId, code: permissionCode);

        var result = role.EnsureCanHold(permission);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(RoleErrors.PermissionNotForApplication.Code);
        result.FirstError.Type.Should().Be(ErrorType.Validation);
    }
}
