using Auth.Application.Features.Applications.GetApplicationOrganizations;
using Auth.Application.Features.Applications.GetApplicationUsers;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement.Queries;

/// <summary>
/// Unit tests for GetApplicationUsersQueryHandler.
/// </summary>
public class GetApplicationUsersQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly GetApplicationUsersQueryHandler _handler;

    public GetApplicationUsersQueryHandlerTests()
    {
        _handler = new GetApplicationUsersQueryHandler(
            _applicationRepositoryMock.Object,
            Mock.Of<IImageUrlComposer>(),
            new Mock<ILogger<GetApplicationUsersQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidApplication_ReturnsPagedUsers()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId);
        var row = new ApplicationUserRow
        {
            UserId = Guid.NewGuid(),
            Email = "member@test.com",
            FirstName = "Mia",
            LastName = "Member",
            Status = UserStatus.Active,
            LastLoginAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            RoleNames = "Administrator, Editor"
        };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.GetUsersPagedAsync(appId, 2, 10, "mia", "email", SortDirection.Desc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([row], 11));

        // Act
        var result = await _handler.Handle(
            new GetApplicationUsersQuery(appId, PageNumber: 2, PageSize: 10, SearchTerm: "mia", SortBy: "email", SortDirection: SortDirection.Desc),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.TotalCount.Should().Be(11);
        result.Value.PageNumber.Should().Be(2);
        result.Value.TotalPages.Should().Be(2);
        result.Value.Users.Should().HaveCount(1);
        result.Value.Users[0].Email.Should().Be("member@test.com");
        result.Value.Users[0].FullName.Should().Be("Mia Member");
        result.Value.Users[0].RoleNames.Should().Be("Administrator, Editor");
    }

    [Fact]
    public async Task Handle_ApplicationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var appId = Guid.NewGuid();

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(new GetApplicationUsersQuery(appId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.NotFound");
    }
}

/// <summary>
/// Unit tests for GetApplicationOrganizationsQueryHandler.
/// </summary>
public class GetApplicationOrganizationsQueryHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly GetApplicationOrganizationsQueryHandler _handler;

    public GetApplicationOrganizationsQueryHandlerTests()
    {
        _handler = new GetApplicationOrganizationsQueryHandler(
            _applicationRepositoryMock.Object,
            Mock.Of<IImageUrlComposer>(),
            new Mock<ILogger<GetApplicationOrganizationsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidApplication_ReturnsPagedOrganizations()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var enabledAt = DateTime.UtcNow.AddDays(-10);
        var application = TestHelpers.CreateApplication(id: appId);
        var row = new ApplicationOrganizationRow(
            Guid.NewGuid(), "acme", "Acme Corp", null,
            OrganizationIsActive: true, LinkIsActive: false, enabledAt, null, MemberCount: 5);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.GetOrganizationsPagedAsync(appId, 1, 20, null, null, SortDirection.Asc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([row], 1));

        // Act
        var result = await _handler.Handle(new GetApplicationOrganizationsQuery(appId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Organizations.Should().HaveCount(1);
        var dto = result.Value.Organizations[0];
        dto.Code.Should().Be("acme");
        dto.OrganizationIsActive.Should().BeTrue();
        dto.IsActive.Should().BeFalse();
        dto.EnabledAt.Should().Be(enabledAt);
        dto.MemberCount.Should().Be(5);
    }

    [Fact]
    public async Task Handle_ApplicationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var appId = Guid.NewGuid();

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(new GetApplicationOrganizationsQuery(appId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.NotFound");
    }
}
