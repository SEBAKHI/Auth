using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Guards the fix for cross-application claim bleed: a token minted for one
/// application carried the user's entire platform authority, so an application
/// enforced permissions it had never issued.
/// </summary>
public class TokenClaimsResolverTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly TokenClaimsResolver _resolver;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _appA = Guid.NewGuid();
    private readonly Guid _appB = Guid.NewGuid();

    public TokenClaimsResolverTests()
    {
        _resolver = new TokenClaimsResolver(
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _organizationRepositoryMock.Object);
    }

    [Fact]
    public async Task ResolveAsync_WithoutApplication_ReturnsPlatformAuthority()
    {
        // Arrange
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateRole(code: "PLATFORM_ADMIN")]);
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["users:read"]);
        _organizationRepositoryMock
            .Setup(r => r.GetMembershipPermissionCodesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(Guid.NewGuid(), "org:members:read")]);

        // Act
        var claims = await _resolver.ResolveAsync(_userId, null, CancellationToken.None);

        // Assert
        claims.RoleCodes.Should().ContainSingle().Which.Should().Be("PLATFORM_ADMIN");
        claims.Permissions.Should().ContainSingle().Which.Should().Be("users:read");
        claims.OrganizationPermissions.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolveAsync_WithApplication_ExcludesAnotherApplicationsRoles()
    {
        // Arrange — the user holds a role in application A. Asking for a token
        // scoped to application B must not surface it: this is the bleed.
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateRole(applicationId: _appA, code: "A_ADMIN")]);
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForApplicationAsync(_userId, _appB, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupScopedPermissions(_appB);

        // Act
        var claims = await _resolver.ResolveAsync(_userId, _appB, CancellationToken.None);

        // Assert
        claims.RoleCodes.Should().BeEmpty();
        _roleRepositoryMock.Verify(
            r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the unscoped role query is what leaked another application's roles");
    }

    [Fact]
    public async Task ResolveAsync_WithApplication_ReturnsThatApplicationsRoles()
    {
        // Arrange
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForApplicationAsync(_userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateRole(applicationId: _appA, code: "A_ADMIN")]);
        SetupScopedPermissions(_appA, "a:read");

        // Act
        var claims = await _resolver.ResolveAsync(_userId, _appA, CancellationToken.None);

        // Assert
        claims.RoleCodes.Should().ContainSingle().Which.Should().Be("A_ADMIN");
        claims.Permissions.Should().ContainSingle().Which.Should().Be("a:read");
    }

    [Fact]
    public async Task ResolveAsync_WithApplication_TakesPermissionsFromTheApplicationScopedQuery()
    {
        // The flat claim comes from the APPLICATION-SCOPED direct grants and
        // nothing else. This used to go through the permission checker, which
        // unioned in every organization's delegated permissions - flat, with no
        // record of which organization granted what. See
        // ResolveAsync_WithApplication_NeverFlattensADelegatedPermissionIntoTheUnscopedClaim.
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForApplicationAsync(_userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupScopedPermissions(_appA, "a:read", "a:write");

        // Act
        var claims = await _resolver.ResolveAsync(_userId, _appA, CancellationToken.None);

        // Assert
        claims.Permissions.Should().BeEquivalentTo(["a:read", "a:write"]);
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the unscoped permission query is what leaked platform authority");
    }

    [Fact]
    public async Task ResolveAsync_WithApplication_FiltersOrganizationClaimsToThatApplication()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForApplicationAsync(_userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupScopedPermissions(_appA);
        _organizationRepositoryMock
            .Setup(r => r.GetMembershipPermissionCodesForApplicationAsync(
                _userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(orgId, "org:members:read")]);

        // Act
        var claims = await _resolver.ResolveAsync(_userId, _appA, CancellationToken.None);

        // Assert
        claims.OrganizationPermissions.Should().ContainSingle()
            .Which.OrganizationId.Should().Be(orgId);
        _organizationRepositoryMock.Verify(
            r => r.GetMembershipPermissionCodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an application token has no business carrying the user's whole organization graph");
    }

    private void SetupScopedPermissions(Guid applicationId, params string[] codes)
    {
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.ToList());
        _organizationRepositoryMock
            .Setup(r => r.GetMembershipPermissionCodesForApplicationAsync(
                _userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionPairsForApplicationAsync(
                _userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task ResolveAsync_WithApplication_NeverFlattensADelegatedPermissionIntoTheUnscopedClaim()
    {
        // The defect this asserts against: a user in TWO organizations that both
        // enable the application received ONE token whose flat "permissions"
        // claim contained every delegated code from both, with nothing recording
        // which organization granted which. The SDK authorizes on that claim, so
        // a permission granted in org A was spendable on org B's data.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForApplicationAsync(_userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupScopedPermissions(_appA, "app:wide:read");

        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionPairsForApplicationAsync(
                _userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(orgA, "invoices:delete"), (orgB, "invoices:read")]);

        var claims = await _resolver.ResolveAsync(_userId, _appA, CancellationToken.None);

        // The flat claim carries application-wide authority and nothing else.
        claims.Permissions.Should().BeEquivalentTo(["app:wide:read"]);
        claims.Permissions.Should().NotContain("invoices:delete",
            "a permission granted inside one organization must never appear unscoped");

        // Each delegated permission is present, tagged with its own organization.
        claims.OrganizationPermissions.Should().BeEquivalentTo(
            [(orgA, "invoices:delete"), (orgB, "invoices:read")]);
    }

    [Fact]
    public async Task ResolveAsync_WithApplication_CombinesMembershipAndDelegatedOrganizationClaims()
    {
        // Membership authority ('org:%' codes from the membership role) and
        // delegated authority (any code, granted per organization per
        // application) are separate queries and must both reach the token.
        var orgId = Guid.NewGuid();

        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForApplicationAsync(_userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupScopedPermissions(_appA);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipPermissionCodesForApplicationAsync(
                _userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(orgId, "org:members:read")]);
        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionPairsForApplicationAsync(
                _userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(orgId, "invoices:read")]);

        var claims = await _resolver.ResolveAsync(_userId, _appA, CancellationToken.None);

        claims.OrganizationPermissions.Should().BeEquivalentTo(
            [(orgId, "org:members:read"), (orgId, "invoices:read")]);
    }

    [Fact]
    public async Task ResolveAsync_WithApplication_DeduplicatesOverlappingOrganizationClaims()
    {
        // The same code can arrive from both sources; the token should carry it
        // once rather than growing a duplicate claim per source.
        var orgId = Guid.NewGuid();

        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForApplicationAsync(_userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupScopedPermissions(_appA);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipPermissionCodesForApplicationAsync(
                _userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(orgId, "org:members:read")]);
        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionPairsForApplicationAsync(
                _userId, _appA, It.IsAny<CancellationToken>()))
            .ReturnsAsync([(orgId, "org:members:read")]);

        var claims = await _resolver.ResolveAsync(_userId, _appA, CancellationToken.None);

        claims.OrganizationPermissions.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolveAsync_WithoutApplication_DoesNotAskForDelegatedPairs()
    {
        // A platform token resolves authority from the platform-scoped queries;
        // the per-application delegated pairs have no meaning there.
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _organizationRepositoryMock
            .Setup(r => r.GetMembershipPermissionCodesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _resolver.ResolveAsync(_userId, null, CancellationToken.None);

        _organizationRepositoryMock.Verify(
            r => r.GetEffectivePermissionPairsForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
