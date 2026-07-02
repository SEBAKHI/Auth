using Auth.Application.Features.Applications.GetApplicationById;
using Auth.Application.Features.Applications.GetApplications;
using Auth.Application.Features.Applications.GetApplicationRoles;
using Auth.Application.Features.Applications.GetApplicationPermissions;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement.Queries;

/// <summary>
/// Unit tests for GetApplicationByIdQueryHandler.
/// </summary>
public class GetApplicationByIdQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<GetApplicationByIdQueryHandler>> _loggerMock;
    private readonly GetApplicationByIdQueryHandler _handler;

    public GetApplicationByIdQueryHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<GetApplicationByIdQueryHandler>>();

        _handler = new GetApplicationByIdQueryHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidId_ReturnsDto()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(
            id: appId,
            code: "CRM",
            name: "CRM Application",
            description: "Customer Relationship Management",
            baseUrl: "https://crm.example.com");

        var query = new GetApplicationByIdQuery(Id: appId);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(appId);
        result.Value.Code.Should().Be(application.Code);
        result.Value.Name.Should().Be("CRM Application");
        result.Value.Description.Should().Be("Customer Relationship Management");
        result.Value.BaseUrl.Should().Be("https://crm.example.com");
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var query = new GetApplicationByIdQuery(Id: appId);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");
    }
}

/// <summary>
/// Unit tests for GetApplicationsQueryHandler.
/// </summary>
public class GetApplicationsQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<GetApplicationsQueryHandler>> _loggerMock;
    private readonly GetApplicationsQueryHandler _handler;

    public GetApplicationsQueryHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<GetApplicationsQueryHandler>>();

        _handler = new GetApplicationsQueryHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidQuery_ReturnsPagedResults()
    {
        // Arrange
        var app1 = TestHelpers.CreateApplication(code: "CRM", name: "CRM App");
        var app2 = TestHelpers.CreateApplication(code: "ERP", name: "ERP App");
        var applications = new List<ApplicationEntity> { app1, app2 };

        var query = new GetApplicationsQuery(
            PageNumber: 1,
            PageSize: 10,
            Search: null,
            IsActive: true);

        _applicationRepositoryMock
            .Setup(r => r.GetPagedAsync(
                query.PageNumber,
                query.PageSize,
                query.Search,
                query.IsActive,
                It.IsAny<string?>(),
                It.IsAny<SortDirection>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((applications as IReadOnlyList<ApplicationEntity>, 2));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Applications.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
        result.Value.PageNumber.Should().Be(1);
        result.Value.PageSize.Should().Be(10);
        result.Value.Applications[0].Code.Should().Be(app1.Code);
        result.Value.Applications[1].Code.Should().Be(app2.Code);
    }
}

/// <summary>
/// Unit tests for GetApplicationRolesQueryHandler.
/// </summary>
public class GetApplicationRolesQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<GetApplicationRolesQueryHandler>> _loggerMock;
    private readonly GetApplicationRolesQueryHandler _handler;

    public GetApplicationRolesQueryHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<GetApplicationRolesQueryHandler>>();

        _handler = new GetApplicationRolesQueryHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidId_ReturnsRoles()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");

        var role1 = TestHelpers.CreateRole(applicationId: appId, code: "CRM-ADMIN", name: "CRM Admin");
        var role2 = TestHelpers.CreateRole(applicationId: appId, code: "CRM-USER", name: "CRM User");
        var roles = new List<Role> { role1, role2 };

        var query = new GetApplicationRolesQuery(ApplicationId: appId);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.GetRolesAsync(appId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles as IReadOnlyList<Role>);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Should().HaveCount(2);
        result.Value[0].Code.Should().Be("CRM-ADMIN");
        result.Value[0].Name.Should().Be("CRM Admin");
        result.Value[0].ApplicationId.Should().Be(appId);
        result.Value[1].Code.Should().Be("CRM-USER");
        result.Value[1].Name.Should().Be("CRM User");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var query = new GetApplicationRolesQuery(ApplicationId: appId);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _applicationRepositoryMock.Verify(
            r => r.GetRolesAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for GetApplicationPermissionsQueryHandler.
/// </summary>
public class GetApplicationPermissionsQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<GetApplicationPermissionsQueryHandler>> _loggerMock;
    private readonly GetApplicationPermissionsQueryHandler _handler;

    public GetApplicationPermissionsQueryHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<GetApplicationPermissionsQueryHandler>>();

        _handler = new GetApplicationPermissionsQueryHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidId_ReturnsPermissions()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");

        var perm1 = TestHelpers.CreatePermission(applicationId: appId, code: "crm:read", name: "CRM Read");
        var perm2 = TestHelpers.CreatePermission(applicationId: appId, code: "crm:write", name: "CRM Write");
        var permissions = new List<Permission> { perm1, perm2 };

        var query = new GetApplicationPermissionsQuery(ApplicationId: appId);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.GetPermissionsAsync(appId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions as IReadOnlyList<Permission>);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Should().HaveCount(2);
        result.Value[0].Code.Should().Be(perm1.Code);
        result.Value[0].Name.Should().Be("CRM Read");
        result.Value[0].ApplicationId.Should().Be(appId);
        result.Value[1].Code.Should().Be(perm2.Code);
        result.Value[1].Name.Should().Be("CRM Write");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var query = new GetApplicationPermissionsQuery(ApplicationId: appId);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _applicationRepositoryMock.Verify(
            r => r.GetPermissionsAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
