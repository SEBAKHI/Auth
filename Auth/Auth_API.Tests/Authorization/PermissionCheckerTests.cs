using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authorization;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// Unit tests for PermissionChecker.
/// Tests both direct user permissions and organization-based permissions.
/// </summary>
public class PermissionCheckerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly PermissionChecker _permissionChecker;

    public PermissionCheckerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();

        _permissionChecker = new PermissionChecker(
            _permissionRepositoryMock.Object,
            _organizationRepositoryMock.Object);
    }

    #region HasPermissionAsync Tests

    [Fact]
    public async Task HasPermissionAsync_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "users:read";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:read", "users:create" });

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithoutPermission_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "users:delete";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:read", "users:create" });

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, null, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithGlobalWildcard_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "any:permission:here";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "*" });

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithPrefixWildcard_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "users:profiles:read";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:*" });

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithPrefixWildcard_MatchesBasePermission()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "users";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:*" });

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithPrefixWildcard_DoesNotMatchDifferentPrefix()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "roles:read";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:*" });

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, null, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_IsCaseInsensitive()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "USERS:READ";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:read" });

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region HasAnyPermissionAsync Tests

    [Fact]
    public async Task HasAnyPermissionAsync_WithOneMatching_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new[] { "users:read", "users:create", "users:delete" };

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:create" });

        // Act
        var result = await _permissionChecker.HasAnyPermissionAsync(userId, permissions, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAnyPermissionAsync_WithNoneMatching_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new[] { "users:read", "users:create" };

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "roles:read" });

        // Act
        var result = await _permissionChecker.HasAnyPermissionAsync(userId, permissions, null, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region HasAllPermissionsAsync Tests

    [Fact]
    public async Task HasAllPermissionsAsync_WithAllMatching_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new[] { "users:read", "users:create" };

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:read", "users:create", "users:delete" });

        // Act
        var result = await _permissionChecker.HasAllPermissionsAsync(userId, permissions, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAllPermissionsAsync_WithSomeMissing_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new[] { "users:read", "users:create", "users:delete" };

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:read", "users:create" });

        // Act
        var result = await _permissionChecker.HasAllPermissionsAsync(userId, permissions, null, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAllPermissionsAsync_WithWildcard_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new[] { "users:read", "users:create", "users:delete" };

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "users:*" });

        // Act
        var result = await _permissionChecker.HasAllPermissionsAsync(userId, permissions, null, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Organization-Based Permissions Tests

    // The organization branch is one query across every membership the user has
    // in which the application is enabled — the repository owns "which
    // organizations count", so these tests state its result rather than walking
    // memberships one by one.

    /// <summary>Stubs the organization branch for an application-scoped call.</summary>
    private void SetupOrganizationPermissions(Guid userId, Guid applicationId, params string[] codes)
    {
        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionCodesForApplicationAsync(
                userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.ToList());
    }

    [Fact]
    public async Task GetUserPermissionsAsync_CombinesDirectAndOrgPermissions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "direct:permission1" });

        SetupOrganizationPermissions(userId, applicationId, "org:permission1", "org:permission2");

        // Act
        var result = await _permissionChecker.GetUserPermissionsAsync(userId, applicationId, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("direct:permission1");
        result.Should().Contain("org:permission1");
        result.Should().Contain("org:permission2");
    }

    [Fact]
    public async Task GetUserPermissionsAsync_SkipsOrgPermissionsWhenAppNotEnabled()
    {
        // Arrange — the application is enabled for none of the user's
        // organizations, so the organization branch contributes nothing.
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "direct:permission1" });

        SetupOrganizationPermissions(userId, applicationId);

        // Act
        var result = await _permissionChecker.GetUserPermissionsAsync(userId, applicationId, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain("direct:permission1");
    }

    [Fact]
    public async Task GetUserPermissionsAsync_DeduplicatesPermissions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "shared:permission", "direct:only" });

        SetupOrganizationPermissions(userId, applicationId, "shared:permission", "org:only");

        // Act
        var result = await _permissionChecker.GetUserPermissionsAsync(userId, applicationId, CancellationToken.None);

        // Assert - should have 3 unique permissions, not 4
        result.Should().HaveCount(3);
        result.Should().Contain("shared:permission");
        result.Should().Contain("direct:only");
        result.Should().Contain("org:only");
    }

    [Fact]
    public async Task GetUserPermissionsAsync_CollectsFromMultipleOrgs()
    {
        // Arrange — permissions from two different organizations, aggregated by
        // the repository into one result set.
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        SetupOrganizationPermissions(userId, applicationId, "org1:permission", "org2:permission");

        // Act
        var result = await _permissionChecker.GetUserPermissionsAsync(userId, applicationId, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("org1:permission");
        result.Should().Contain("org2:permission");
    }

    [Fact]
    public async Task HasPermissionAsync_WithOrgBasedPermission_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var permission = "data-transfer:export";

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>()); // No direct permissions

        SetupOrganizationPermissions(userId, applicationId, "data-transfer:export", "data-transfer:import");

        // Act
        var result = await _permissionChecker.HasPermissionAsync(userId, permission, applicationId, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WithoutApplicationId_DoesNotCheckOrgPermissions()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "some:permission" });

        // Act
        var result = await _permissionChecker.GetUserPermissionsAsync(userId, applicationId: null, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain("some:permission");

        // A platform token asks nothing of the organization branch.
        _organizationRepositoryMock.Verify(
            r => r.GetEffectivePermissionCodesForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    public async Task HasPermissionAsync_PropagatesCancellationToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, cancellationToken))
            .ReturnsAsync(new List<string> { "test:permission" });

        // Act
        await _permissionChecker.HasPermissionAsync(userId, "test:permission", null, cancellationToken);

        // Assert
        _permissionRepositoryMock.Verify(
            r => r.GetUserEffectivePermissionsAsync(userId, cancellationToken),
            Times.Once);
    }

    #endregion
}
