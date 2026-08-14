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
    private readonly Mock<IPermissionChecker> _permissionCheckerMock = new();
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
            _permissionCheckerMock.Object,
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
    public async Task ResolveAsync_WithApplication_TakesPermissionsFromTheScopedChecker()
    {
        // Arrange — the checker is the one component that unions the
        // application-scoped direct grants with the organization-mediated ones,
        // so the resolver must go through it rather than query directly.
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
        _permissionCheckerMock
            .Setup(c => c.GetUserPermissionsAsync(_userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.ToList());
        _organizationRepositoryMock
            .Setup(r => r.GetMembershipPermissionCodesForApplicationAsync(
                _userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }
}
