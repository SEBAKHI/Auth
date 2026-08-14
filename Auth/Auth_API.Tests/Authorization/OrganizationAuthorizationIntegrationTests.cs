using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authorization;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// Integration tests for organization-based authorization flows.
/// These tests verify end-to-end authorization scenarios involving
/// organizations, app subscriptions, roles, and permissions.
/// </summary>
public class OrganizationAuthorizationIntegrationTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly PermissionChecker _permissionChecker;

    // Common test data
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _dataTransferAppId = Guid.NewGuid();
    private readonly Guid _mailMergeAppId = Guid.NewGuid();
    private readonly Guid _acmeOrgId = Guid.NewGuid();
    private readonly Guid _betaCorpOrgId = Guid.NewGuid();

    // A small stand-in for the organization tables. The checker now asks one
    // repository method for "this user's permissions for this application
    // across every organization that has it enabled", so these tests describe
    // the rows and let the stub compute the same answer the SQL would — the
    // scenarios stay readable and keep asserting real semantics instead of a
    // call sequence.
    private readonly Dictionary<(Guid User, Guid Organization), bool> _memberships = [];
    private readonly Dictionary<(Guid Organization, Guid Application), bool> _appEnabled = [];
    private readonly Dictionary<(Guid Organization, Guid User, Guid Application), string[]> _orgPermissions = [];

    public OrganizationAuthorizationIntegrationTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();

        _permissionChecker = new PermissionChecker(
            _permissionRepositoryMock.Object,
            _organizationRepositoryMock.Object);

        // Default: no direct permissions
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionCodesForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, Guid applicationId, CancellationToken _) =>
                AggregateOrgPermissions(userId, applicationId));
    }

    /// <summary>
    /// Mirrors GetEffectivePermissionCodesForApplicationAsync: union the codes
    /// from every active membership whose organization has the application
    /// enabled.
    /// </summary>
    private IReadOnlyList<string> AggregateOrgPermissions(Guid userId, Guid applicationId)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ((user, organization), isActive) in _memberships)
        {
            if (user != userId || !isActive)
            {
                continue;
            }

            if (!_appEnabled.TryGetValue((organization, applicationId), out var enabled) || !enabled)
            {
                continue;
            }

            if (_orgPermissions.TryGetValue((organization, userId, applicationId), out var granted))
            {
                foreach (var code in granted)
                {
                    codes.Add(code);
                }
            }
        }

        return codes.ToList();
    }

    #region Scenario: User Accesses App Through Single Organization

    [Fact]
    public async Task UserAccessesApp_WhenMemberOfOrgWithAppEnabledAndPermission_ReturnsTrue()
    {
        // Scenario: Jane is a member of Acme Corp, which has Data Transfer enabled.
        // Jane has been assigned the "data-transfer:read" permission.
        // Jane should be able to access Data Transfer.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read", "data-transfer:export" });

        // Act
        var canRead = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:read", _dataTransferAppId, CancellationToken.None);
        var canExport = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:export", _dataTransferAppId, CancellationToken.None);
        var canDelete = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:delete", _dataTransferAppId, CancellationToken.None);

        // Assert
        canRead.Should().BeTrue("Jane has data-transfer:read permission");
        canExport.Should().BeTrue("Jane has data-transfer:export permission");
        canDelete.Should().BeFalse("Jane does not have data-transfer:delete permission");
    }

    [Fact]
    public async Task UserAccessesApp_WhenMemberOfOrgButAppNotEnabled_ReturnsFalse()
    {
        // Scenario: Jane is a member of Acme Corp, but Acme Corp hasn't enabled Mail Merge.
        // Even if Jane has permissions assigned, she shouldn't access the app.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _mailMergeAppId, enabled: false);

        // Even with permissions assigned (which shouldn't happen in practice)
        SetupOrgUserPermissions(_acmeOrgId, _userId, _mailMergeAppId,
            new[] { "mail-merge:read" });

        // Act
        var canRead = await _permissionChecker.HasPermissionAsync(
            _userId, "mail-merge:read", _mailMergeAppId, CancellationToken.None);

        // Assert
        canRead.Should().BeFalse("App is not enabled for the organization");
    }

    [Fact]
    public async Task UserAccessesApp_WhenMemberOfOrgButNoPermissions_ReturnsFalse()
    {
        // Scenario: Jane is a member of Acme Corp, which has Data Transfer enabled.
        // But Jane hasn't been assigned any permissions for Data Transfer.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            Array.Empty<string>()); // No permissions

        // Act
        var canRead = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:read", _dataTransferAppId, CancellationToken.None);

        // Assert
        canRead.Should().BeFalse("Jane has no permissions for this app");
    }

    [Fact]
    public async Task UserAccessesApp_WhenMembershipInactive_ReturnsFalse()
    {
        // Scenario: Jane's membership in Acme Corp has been deactivated.
        // She should no longer have access.

        // Arrange — the membership exists but is deactivated, so it contributes
        // nothing even though the organization has the application enabled.
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: false);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read" });

        // Act
        var canRead = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:read", _dataTransferAppId, CancellationToken.None);

        // Assert
        canRead.Should().BeFalse("User has no active memberships");
    }

    #endregion

    #region Scenario: User in Multiple Organizations

    [Fact]
    public async Task UserInMultipleOrgs_GetsAggregatedPermissions()
    {
        // Scenario: Jane is a member of both Acme Corp and Beta Corp.
        // Acme Corp grants her read permission for Data Transfer.
        // Beta Corp grants her write permission for Data Transfer.
        // Jane should have both permissions.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupUserInOrganization(_userId, _betaCorpOrgId, isActive: true);

        // Both orgs have Data Transfer enabled
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgAppEnabled(_betaCorpOrgId, _dataTransferAppId, enabled: true);

        // Different permissions in each org
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read" });
        SetupOrgUserPermissions(_betaCorpOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:write" });

        // Act
        var permissions = await _permissionChecker.GetUserPermissionsAsync(
            _userId, _dataTransferAppId, CancellationToken.None);
        var canRead = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:read", _dataTransferAppId, CancellationToken.None);
        var canWrite = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:write", _dataTransferAppId, CancellationToken.None);

        // Assert
        permissions.Should().HaveCount(2);
        canRead.Should().BeTrue("Read granted by Acme Corp");
        canWrite.Should().BeTrue("Write granted by Beta Corp");
    }

    [Fact]
    public async Task UserInMultipleOrgs_OnlyOneOrgHasAppEnabled_GetsPermissionsFromEnabledOrg()
    {
        // Scenario: Jane is in both Acme Corp and Beta Corp.
        // Only Acme Corp has Data Transfer enabled.
        // Jane should only get permissions from Acme Corp.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupUserInOrganization(_userId, _betaCorpOrgId, isActive: true);

        // Only Acme has Data Transfer
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgAppEnabled(_betaCorpOrgId, _dataTransferAppId, enabled: false);

        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read" });

        // Act
        var permissions = await _permissionChecker.GetUserPermissionsAsync(
            _userId, _dataTransferAppId, CancellationToken.None);

        // Assert
        // Beta Corp's grant is excluded because the application is not enabled
        // there — asserted on the result, since the disabled organization is
        // filtered inside the query rather than skipped by the caller.
        permissions.Should().ContainSingle()
            .Which.Should().Be("data-transfer:read");
    }

    [Fact]
    public async Task UserInMultipleOrgs_DifferentAppsInDifferentOrgs_CorrectPermissionSeparation()
    {
        // Scenario: Jane is in both Acme Corp and Beta Corp.
        // Acme Corp has Data Transfer, Beta Corp has Mail Merge.
        // Permissions should be correctly isolated per app.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupUserInOrganization(_userId, _betaCorpOrgId, isActive: true);

        // Acme has Data Transfer, Beta has Mail Merge
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgAppEnabled(_acmeOrgId, _mailMergeAppId, enabled: false);
        SetupOrgAppEnabled(_betaCorpOrgId, _dataTransferAppId, enabled: false);
        SetupOrgAppEnabled(_betaCorpOrgId, _mailMergeAppId, enabled: true);

        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read" });
        SetupOrgUserPermissions(_betaCorpOrgId, _userId, _mailMergeAppId,
            new[] { "mail-merge:send" });

        // Act
        var dtPermissions = await _permissionChecker.GetUserPermissionsAsync(
            _userId, _dataTransferAppId, CancellationToken.None);
        var mmPermissions = await _permissionChecker.GetUserPermissionsAsync(
            _userId, _mailMergeAppId, CancellationToken.None);

        // Assert
        dtPermissions.Should().ContainSingle()
            .Which.Should().Be("data-transfer:read");
        mmPermissions.Should().ContainSingle()
            .Which.Should().Be("mail-merge:send");
    }

    #endregion

    #region Scenario: Wildcard Permissions Through Organization

    [Fact]
    public async Task UserWithOrgWildcardPermission_HasAccessToAllSubPermissions()
    {
        // Scenario: Jane has "data-transfer:*" permission in her org.
        // She should have access to any data-transfer permission.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:*" });

        // Act
        var canRead = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:read", _dataTransferAppId, CancellationToken.None);
        var canWrite = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:write", _dataTransferAppId, CancellationToken.None);
        var canAdmin = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:admin:settings", _dataTransferAppId, CancellationToken.None);
        var canOther = await _permissionChecker.HasPermissionAsync(
            _userId, "mail-merge:read", _dataTransferAppId, CancellationToken.None);

        // Assert
        canRead.Should().BeTrue();
        canWrite.Should().BeTrue();
        canAdmin.Should().BeTrue();
        canOther.Should().BeFalse("Wildcard doesn't match different prefix");
    }

    [Fact]
    public async Task UserWithGlobalWildcard_HasAccessToEverything()
    {
        // Scenario: Jane has "*" permission (super admin in org).
        // She should have access to any permission.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "*" });

        // Act
        var canAnything = await _permissionChecker.HasPermissionAsync(
            _userId, "absolutely:anything:here", _dataTransferAppId, CancellationToken.None);

        // Assert
        canAnything.Should().BeTrue("Global wildcard grants everything");
    }

    [Fact]
    public async Task UserWithNestedWildcard_MatchesCorrectly()
    {
        // Scenario: Jane has "data-transfer:reports:*" permission.
        // She should only match report-related permissions.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:reports:*" });

        // Act
        var canViewReports = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:reports:view", _dataTransferAppId, CancellationToken.None);
        var canExportReports = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:reports:export", _dataTransferAppId, CancellationToken.None);
        var canRead = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:read", _dataTransferAppId, CancellationToken.None);

        // Assert
        canViewReports.Should().BeTrue();
        canExportReports.Should().BeTrue();
        canRead.Should().BeFalse("Not covered by reports:* wildcard");
    }

    #endregion

    #region Scenario: Direct Permissions Combined with Organization Permissions

    [Fact]
    public async Task UserWithBothDirectAndOrgPermissions_GetsCombinedPermissions()
    {
        // Scenario: Jane has direct platform permissions AND org-based permissions.
        // Both should be combined.

        // Arrange
        // Direct permissions (e.g., platform admin)
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_userId, _dataTransferAppId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "platform:admin", "direct:permission" });

        // Org-based permissions
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read", "data-transfer:write" });

        // Act
        var permissions = await _permissionChecker.GetUserPermissionsAsync(_userId, _dataTransferAppId, CancellationToken.None);

        // Assert
        permissions.Should().HaveCount(4);
        permissions.Should().Contain("platform:admin");
        permissions.Should().Contain("direct:permission");
        permissions.Should().Contain("data-transfer:read");
        permissions.Should().Contain("data-transfer:write");
    }

    [Fact]
    public async Task UserWithOverlappingDirectAndOrgPermissions_DeduplicatesCorrectly()
    {
        // Scenario: Jane has the same permission both directly and through org.
        // Should be deduplicated.

        // Arrange
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_userId, _dataTransferAppId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "data-transfer:read", "platform:audit" });

        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read", "data-transfer:write" }); // read is duplicate

        // Act
        var permissions = await _permissionChecker.GetUserPermissionsAsync(_userId, _dataTransferAppId, CancellationToken.None);

        // Assert
        permissions.Should().HaveCount(3);
        permissions.Count(p => p == "data-transfer:read").Should().Be(1);
    }

    #endregion

    #region Scenario: HasAnyPermission and HasAllPermissions Through Org

    [Fact]
    public async Task HasAnyPermissionAsync_WithOrgBasedPermissions_ReturnsCorrectly()
    {
        // Scenario: Jane has org-based permissions. HasAnyPermission should work correctly.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read" });

        // Act
        var hasAny = await _permissionChecker.HasAnyPermissionAsync(
            _userId,
            new[] { "data-transfer:read", "data-transfer:write", "data-transfer:delete" },
            _dataTransferAppId,
            CancellationToken.None);

        var hasNone = await _permissionChecker.HasAnyPermissionAsync(
            _userId,
            new[] { "data-transfer:admin", "data-transfer:delete" },
            _dataTransferAppId,
            CancellationToken.None);

        // Assert
        hasAny.Should().BeTrue("Has at least one of the permissions");
        hasNone.Should().BeFalse("Has none of the permissions");
    }

    [Fact]
    public async Task HasAllPermissionsAsync_WithOrgBasedPermissions_ReturnsCorrectly()
    {
        // Scenario: Jane has org-based permissions. HasAllPermissions should work correctly.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read", "data-transfer:write", "data-transfer:export" });

        // Act
        var hasAll = await _permissionChecker.HasAllPermissionsAsync(
            _userId,
            new[] { "data-transfer:read", "data-transfer:write" },
            _dataTransferAppId,
            CancellationToken.None);

        var missingOne = await _permissionChecker.HasAllPermissionsAsync(
            _userId,
            new[] { "data-transfer:read", "data-transfer:delete" },
            _dataTransferAppId,
            CancellationToken.None);

        // Assert
        hasAll.Should().BeTrue("Has all required permissions");
        missingOne.Should().BeFalse("Missing data-transfer:delete");
    }

    [Fact]
    public async Task HasAllPermissionsAsync_WithWildcardInOrg_CoversAllRequired()
    {
        // Scenario: Jane has wildcard permission through org.
        // Should satisfy HasAllPermissions.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:*" });

        // Act
        var hasAll = await _permissionChecker.HasAllPermissionsAsync(
            _userId,
            new[] { "data-transfer:read", "data-transfer:write", "data-transfer:delete", "data-transfer:admin" },
            _dataTransferAppId,
            CancellationToken.None);

        // Assert
        hasAll.Should().BeTrue("Wildcard covers all data-transfer permissions");
    }

    #endregion

    #region Scenario: Edge Cases and Error Conditions

    [Fact]
    public async Task UserWithNoMemberships_ReturnsOnlyDirectPermissions()
    {
        // Scenario: User has no org memberships but has direct permissions.

        // Arrange — no memberships recorded, so the organization branch is empty.
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_userId, _dataTransferAppId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "direct:permission" });

        // Act
        var permissions = await _permissionChecker.GetUserPermissionsAsync(_userId, _dataTransferAppId, CancellationToken.None);

        // Assert
        permissions.Should().ContainSingle()
            .Which.Should().Be("direct:permission");
    }

    [Fact]
    public async Task EmptyPermissionCheck_ReturnsFalse()
    {
        // Scenario: Check for empty permission string.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "data-transfer:read" });

        // Act
        var hasEmpty = await _permissionChecker.HasPermissionAsync(
            _userId, "", _dataTransferAppId, CancellationToken.None);

        // Assert
        hasEmpty.Should().BeFalse("Empty permission should never match");
    }

    [Fact]
    public async Task CaseInsensitivePermissionMatching_WorksThroughOrg()
    {
        // Scenario: Permission codes should match case-insensitively.

        // Arrange
        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);
        SetupOrgAppEnabled(_acmeOrgId, _dataTransferAppId, enabled: true);
        SetupOrgUserPermissions(_acmeOrgId, _userId, _dataTransferAppId,
            new[] { "Data-Transfer:READ" });

        // Act
        var canReadLower = await _permissionChecker.HasPermissionAsync(
            _userId, "data-transfer:read", _dataTransferAppId, CancellationToken.None);
        var canReadUpper = await _permissionChecker.HasPermissionAsync(
            _userId, "DATA-TRANSFER:READ", _dataTransferAppId, CancellationToken.None);
        var canReadMixed = await _permissionChecker.HasPermissionAsync(
            _userId, "Data-Transfer:Read", _dataTransferAppId, CancellationToken.None);

        // Assert
        canReadLower.Should().BeTrue();
        canReadUpper.Should().BeTrue();
        canReadMixed.Should().BeTrue();
    }

    #endregion

    #region Scenario: Authorization Without ApplicationId

    [Fact]
    public async Task GetPermissionsWithoutAppId_DoesNotCheckOrgPermissions()
    {
        // Scenario: When no applicationId is provided, should only check direct permissions.
        // Org-based permissions require app context.

        // Arrange
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "global:permission" });

        SetupUserInOrganization(_userId, _acmeOrgId, isActive: true);

        // Act
        var permissions = await _permissionChecker.GetUserPermissionsAsync(_userId, applicationId: null, CancellationToken.None);

        // Assert
        permissions.Should().ContainSingle()
            .Which.Should().Be("global:permission");

        _organizationRepositoryMock.Verify(
            r => r.GetEffectivePermissionCodesForApplicationAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not check org permissions without applicationId");
    }

    #endregion

    #region Helper Methods

    private void SetupUserInOrganization(Guid userId, Guid organizationId, bool isActive)
    {
        _memberships[(userId, organizationId)] = isActive;
    }

    private void SetupOrgAppEnabled(Guid organizationId, Guid applicationId, bool enabled)
    {
        _appEnabled[(organizationId, applicationId)] = enabled;
    }

    private void SetupOrgUserPermissions(Guid organizationId, Guid userId, Guid applicationId, string[] permissions)
    {
        _orgPermissions[(organizationId, userId, applicationId)] = permissions;
    }

    #endregion
}
