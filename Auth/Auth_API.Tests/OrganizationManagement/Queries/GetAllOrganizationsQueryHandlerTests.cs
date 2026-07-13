using Auth.Application.Features.Organizations.GetAllOrganizations;
using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;

namespace Auth_API.Tests.OrganizationManagement.Queries;

/// <summary>
/// Unit tests for GetAllOrganizationsQueryHandler (platform administration).
/// </summary>
public class GetAllOrganizationsQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly GetAllOrganizationsQueryHandler _handler;

    public GetAllOrganizationsQueryHandlerTests()
    {
        _handler = new GetAllOrganizationsQueryHandler(
            _organizationRepositoryMock.Object,
            _userRepositoryMock.Object,
            Mock.Of<Auth.Application.Interfaces.IImageUrlComposer>());
    }

    [Fact]
    public async Task Handle_ReturnsPagedOrganizations_WithCountsAndOwner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var organization = TestHelpers.CreateOrganization(
            code: "acme", name: "Acme", ownerId: ownerId, isActive: true);
        var owner = TestHelpers.CreateUser(
            id: ownerId, email: "owner@acme.com", firstName: "Alice", lastName: "Owner");

        _organizationRepositoryMock
            .Setup(r => r.GetPagedAsync(
                1, 20, null, null, SortDirection.Asc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Organization>)new List<Organization> { organization }, 1));

        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [organization.Id] = 4 });

        _organizationRepositoryMock
            .Setup(r => r.GetEnabledApplicationCountsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [organization.Id] = 2 });

        _userRepositoryMock
            .Setup(r => r.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { owner });

        // Act
        var result = await _handler.Handle(new GetAllOrganizationsQuery(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Organizations.Should().HaveCount(1);
        var dto = result.Value.Organizations[0];
        dto.Code.Should().Be("acme");
        dto.OwnerName.Should().Be("Alice Owner");
        dto.OwnerEmail.Should().Be("owner@acme.com");
        dto.MemberCount.Should().Be(4);
        dto.EnabledAppCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoOrganizations_ReturnsEmptyPage()
    {
        // Arrange
        _organizationRepositoryMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Organization>)new List<Organization>(), 0));

        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        _organizationRepositoryMock
            .Setup(r => r.GetEnabledApplicationCountsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        _userRepositoryMock
            .Setup(r => r.GetByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());

        // Act
        var result = await _handler.Handle(
            new GetAllOrganizationsQuery(PageNumber: 3, PageSize: 50), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Organizations.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
        result.Value.PageNumber.Should().Be(3);
        result.Value.PageSize.Should().Be(50);
    }
}
