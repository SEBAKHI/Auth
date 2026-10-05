using Auth.Application.Common;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement.Commands;

/// <summary>
/// OI-63: the two organization-creation settings on an application. Saving them
/// is where the authority for every later creator-role grant is checked, since
/// the organization-creation step grants the role with no guard of its own.
/// </summary>
public class OrganizationCreationSettingsTests
{
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly ApplicationEntity _application;
    private readonly Guid _roleId = Guid.NewGuid();

    public OrganizationCreationSettingsTests()
    {
        _application = TestHelpers.CreateApplication(code: "EDIS", accessMode: ApplicationAccessMode.Everyone);
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);

        SetupRole(_application.Id, isActive: true, codes: ["edis:exhibitors:manage", "edis:booths:read"]);
        AdminHolds("*");
    }

    private void SetupRole(Guid applicationId, bool isActive, string[] codes)
    {
        _roles.Setup(r => r.GetByIdAsync(_roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: _roleId, applicationId: applicationId, isActive: isActive));
        _permissions.Setup(r => r.GetRolePermissionsAsync(_roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.Select(code => TestHelpers.CreatePermission(code: code)).ToList());
    }

    private void AdminHolds(params string[] codes) =>
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(_admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);

    private UpdateApplicationCommandHandler Handler() => new(
        _applications.Object,
        new Mock<IRefreshTokenRepository>().Object,
        new Mock<IUserSessionRepository>().Object,
        ApplicationTestImages.Composer(),
        new OrganizationCreatorRoleCheck(_roles.Object, _permissions.Object),
        new PermissionGrantGuard(_permissions.Object),
        new Mock<ILogger<UpdateApplicationCommandHandler>>().Object);

    private UpdateApplicationCommand Update(bool? allow, Guid? roleId, string name = "EDIS") =>
        new(_application.Id, name, AccessMode: ApplicationAccessMode.Everyone,
            AllowOrganizationCreation: allow, OrganizationCreatorRoleId: roleId)
        { ModifiedBy = _admin };

    private void VerifyNotSaved() =>
        _applications.Verify(r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task AValidRole_IsSaved_AndReported()
    {
        var result = await Handler().Handle(Update(true, _roleId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.AllowOrganizationCreation.Should().BeTrue();
        result.Value.OrganizationCreatorRoleId.Should().Be(_roleId);
        _applications.Verify(r => r.UpdateAsync(
            It.Is<ApplicationEntity>(a => a.AllowOrganizationCreation && a.OrganizationCreatorRoleId == _roleId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public static TheoryData<string> InvalidRoleCases => new()
    {
        "another application's role", "inactive role", "role without permissions", "allowed without a role",
        "unknown role",
    };

    [Fact]
    public void InvalidRoleCases_AreNotEmpty() => InvalidRoleCases.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(InvalidRoleCases))]
    public async Task AnUnusableRole_IsRefusedWithThePublishedCode(string invalid)
    {
        Guid? roleId = _roleId;
        switch (invalid)
        {
            case "another application's role": SetupRole(Guid.NewGuid(), isActive: true, codes: ["x:y"]); break;
            case "inactive role": SetupRole(_application.Id, isActive: false, codes: ["x:y"]); break;
            case "role without permissions": SetupRole(_application.Id, isActive: true, codes: []); break;
            case "allowed without a role": roleId = null; break;
            case "unknown role": roleId = Guid.NewGuid(); break;
        }

        var result = await Handler().Handle(Update(true, roleId), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.OrganizationCreatorRoleInvalid");
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
        VerifyNotSaved();
    }

    [Fact]
    public async Task AnAdministratorMissingOneOfTheRolesPermissions_IsRefusedByTheGrantGuard()
    {
        AdminHolds("edis:exhibitors:manage");

        var result = await Handler().Handle(Update(true, _roleId), CancellationToken.None);

        result.FirstError.Code.Should().Be(Auth.Domain.Errors.PermissionErrors.CannotGrantHigherPermission.Code);
        VerifyNotSaved();
    }

    [Fact]
    public async Task AnAdministratorHoldingAWildcardOverTheRole_Passes()
    {
        AdminHolds("edis:*");

        var result = await Handler().Handle(Update(true, _roleId), CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task NullForBoth_LeavesTheSettingsUnchanged_TheLogoUploadCase()
    {
        _application.LoadOrganizationCreation(true, _roleId);
        AdminHolds("nothing:relevant");

        var result = await Handler().Handle(Update(null, null, name: "EDIS renamed"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.AllowOrganizationCreation.Should().BeTrue();
        result.Value.OrganizationCreatorRoleId.Should().Be(_roleId);
    }

    [Fact]
    public async Task ResendingTheSameSettings_IsNotRecheckedOnARename()
    {
        _application.LoadOrganizationCreation(true, _roleId);
        AdminHolds("nothing:relevant");

        var result = await Handler().Handle(Update(true, _roleId, name: "EDIS renamed"), CancellationToken.None);

        result.IsError.Should().BeFalse("an unchanged configuration was already checked when it was saved");
        _roles.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SwitchingOff_KeepsTheRole_AndChecksNothing()
    {
        _application.LoadOrganizationCreation(true, _roleId);
        AdminHolds("nothing:relevant");

        var result = await Handler().Handle(Update(false, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.AllowOrganizationCreation.Should().BeFalse();
        result.Value.OrganizationCreatorRoleId.Should().Be(_roleId);
    }

    [Fact]
    public async Task TheEmptyGuid_ClearsTheRole_WhenCreationIsOff()
    {
        _application.LoadOrganizationCreation(false, _roleId);

        var result = await Handler().Handle(Update(null, Guid.Empty), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.OrganizationCreatorRoleId.Should().BeNull();
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(null, "role")]
    public async Task AtCreation_OrganizationCreationIsRefused_BecauseNoRoleOfTheApplicationExistsYet(bool? allow, string? role)
    {
        var applications = new Mock<IApplicationRepository>();
        var handler = new CreateApplicationCommandHandler(
            applications.Object, ApplicationTestImages.Composer(),
            new Mock<ILogger<CreateApplicationCommandHandler>>().Object);

        var result = await handler.Handle(
            new CreateApplicationCommand("NEWAPP", "New", AllowOrganizationCreation: allow,
                OrganizationCreatorRoleId: role is null ? null : Guid.NewGuid()) { CreatedBy = _admin },
            CancellationToken.None);

        result.FirstError.Code.Should().Be("Application.OrganizationCreatorRoleInvalid");
        applications.Verify(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtCreation_TheSettingsDefaultToOff()
    {
        var applications = new Mock<IApplicationRepository>();
        var handler = new CreateApplicationCommandHandler(
            applications.Object, ApplicationTestImages.Composer(),
            new Mock<ILogger<CreateApplicationCommandHandler>>().Object);

        var result = await handler.Handle(new CreateApplicationCommand("NEWAPP", "New") { CreatedBy = _admin }, CancellationToken.None);

        result.Value.AllowOrganizationCreation.Should().BeFalse();
        result.Value.OrganizationCreatorRoleId.Should().BeNull();
    }
}
