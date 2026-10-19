using Auth.Application.Common;
using Auth.Application.Features.Organizations.GetAssignableAppRoles;
using Auth.Application.Features.Organizations.GetMembershipRoles;
using Auth_API.Tests.Helpers;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.OrganizationManagement.Queries;

/// <summary>
/// The organization's role pickers. An organization owner holds no platform
/// permission, so the platform-wide role lists answered 403 and every picker came
/// back empty; these queries answer from inside the organization instead.
/// </summary>
public class GetMembershipRolesQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();

    [Fact]
    public async Task Handle_OffersOrganizationAdminAndMember_NeverTheOwnerOrAPlatformRole()
    {
        var orgId = Guid.NewGuid();
        _orgRepoMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        // The repository is asked for the allowlist only; even if it returned more,
        // the domain predicate filters it, so this returns more on purpose.
        IReadOnlyCollection<string>? asked = null;
        _roleRepoMock
            .Setup(r => r.GetGlobalByCodesAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((codes, _) => asked = codes)
            .ReturnsAsync(
            [
                TestHelpers.CreateRole(code: OrganizationRoleCodes.Admin, name: "Organization Admin"),
                TestHelpers.CreateRole(code: OrganizationRoleCodes.Member, name: "Organization Member"),
                TestHelpers.CreateRole(code: OrganizationRoleCodes.Owner, name: "Organization Owner"),
                TestHelpers.CreateRole(code: "super-admin", name: "Super Administrator"),
                TestHelpers.CreateRole(code: OrganizationRoleCodes.Member, name: "Retired", isActive: false),
            ]);

        var result = await new GetMembershipRolesQueryHandler(_orgRepoMock.Object, _roleRepoMock.Object)
            .Handle(new GetMembershipRolesQuery(orgId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Select(role => role.Code).Should().BeEquivalentTo(
            [OrganizationRoleCodes.Admin, OrganizationRoleCodes.Member]);
        asked.Should().BeEquivalentTo([OrganizationRoleCodes.Admin, OrganizationRoleCodes.Member]);
    }

    [Fact]
    public async Task Handle_UnknownOrganization_ReturnsNotFound()
    {
        var result = await new GetMembershipRolesQueryHandler(_orgRepoMock.Object, _roleRepoMock.Object)
            .Handle(new GetMembershipRolesQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.NotFound");
    }
}

public class GetAssignableAppRolesQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IPermissionRepository> _permRepoMock = new();

    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _appId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();

    private readonly Role _representative;
    private readonly Role _manager;
    private readonly Role _viewer;
    private readonly Role _retired;

    public GetAssignableAppRolesQueryHandlerTests()
    {
        _representative = TestHelpers.CreateRole(applicationId: _appId, code: "sales_rep", name: "Sales representative");
        _manager = TestHelpers.CreateRole(applicationId: _appId, code: "sales_manager", name: "Sales manager");
        _viewer = TestHelpers.CreateRole(applicationId: _appId, code: "viewer", name: "Viewer");
        _retired = TestHelpers.CreateRole(applicationId: _appId, code: "retired", name: "Retired", isActive: false);

        var permissions = new Dictionary<Role, string[]>
        {
            [_representative] = ["crm:leads:read"],
            [_manager] = ["crm:leads:read", "crm:leads:manage", "crm:deals:manage"],
            [_viewer] = [],
            [_retired] = ["crm:leads:read"],
        };

        _orgRepoMock
            .Setup(r => r.GetByIdAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: _orgId, isActive: true));
        _orgRepoMock
            .Setup(r => r.IsApplicationEnabledAsync(_orgId, _appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _appRepoMock
            .Setup(r => r.GetByIdAsync(_appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: _appId));
        _roleRepoMock
            .Setup(r => r.GetByApplicationAsync(_appId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions.Keys.ToList());
        _permRepoMock
            .Setup(r => r.GetApplicationRolePermissionCodesAsync(_appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions.SelectMany(pair => pair.Value.Select(code => (pair.Key.Id, code))).ToList());

        foreach (var (role, codes) in permissions)
        {
            _roleRepoMock
                .Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(role);
            _permRepoMock
                .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(codes.Select(code => TestHelpers.CreatePermission(applicationId: _appId, code: code)).ToList());
        }

        // An organization owner who manages the application through the organization, with
        // nothing on the platform.
        _orgRepoMock
            .Setup(r => r.GetEffectivePermissionCodesAsync(_orgId, _actorId, _appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["crm:leads:read", "crm:leads:manage"]);
        _permRepoMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(_actorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["*"]);
    }

    private OrganizationGrantGuard Guard() => new(_orgRepoMock.Object, _permRepoMock.Object);

    private Task<ErrorOr.ErrorOr<IReadOnlyList<Auth.Application.DTOs.OrganizationRoleOptionDto>>> Pick(bool platformAuthorityInToken = false) =>
        new GetAssignableAppRolesQueryHandler(_orgRepoMock.Object, _roleRepoMock.Object, _permRepoMock.Object, Guard())
            .Handle(
                new GetAssignableAppRolesQuery(_orgId, _appId)
                {
                    RequestedBy = _actorId,
                    PlatformAuthorityInToken = platformAuthorityInToken
                },
                CancellationToken.None);

    [Fact]
    public async Task Handle_OffersOnlyActiveRolesTheCallerHoldsEveryPermissionOf()
    {
        var result = await Pick();

        result.IsError.Should().BeFalse();
        result.Value.Select(role => role.Code).Should().BeEquivalentTo(["sales_rep", "viewer"],
            "the manager role carries crm:deals:manage, which the caller does not hold, and the retired role is inactive");
    }

    [Fact]
    public async Task Handle_PlatformAuthorityCountsOnlyWhenTheTokenCarriesIt()
    {
        // A platform administrator whose platform permissions were withheld from the
        // token (no second factor) must not see more through the organization door.
        var withheld = await Pick(platformAuthorityInToken: false);
        var carried = await Pick(platformAuthorityInToken: true);

        withheld.Value.Select(role => role.Code).Should().NotContain("sales_manager");
        carried.Value.Select(role => role.Code).Should().Contain("sales_manager");
    }

    [Fact]
    public async Task Handle_ApplicationNotEnabledForTheOrganization_ReturnsApplicationNotEnabled()
    {
        _orgRepoMock
            .Setup(r => r.IsApplicationEnabledAsync(_orgId, _appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Pick();

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.ApplicationNotEnabled");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThePicker_OffersExactlyTheActiveRolesTheAssignmentAccepts(bool platformAuthorityInToken)
    {
        // The property the picker exists for: nothing offered is then refused, and
        // nothing the assignment would accept is hidden (among active roles).
        var offered = (await Pick(platformAuthorityInToken)).Value.Select(role => role.Id).ToHashSet();

        var check = new OrganizationApplicationRoleCheck(
            _orgRepoMock.Object, _appRepoMock.Object, _roleRepoMock.Object, _permRepoMock.Object, Guard());

        foreach (var role in new[] { _representative, _manager, _viewer })
        {
            var accepted = await check.EnsureAssignableAsync(
                _orgId, _actorId, _appId, role.Id, platformAuthorityInToken, CancellationToken.None);

            offered.Contains(role.Id).Should().Be(!accepted.IsError, $"{role.Code} is offered exactly when it is accepted");
        }
    }
}
