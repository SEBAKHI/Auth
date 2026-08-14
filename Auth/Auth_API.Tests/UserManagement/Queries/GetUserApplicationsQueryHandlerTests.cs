using Auth.Application.Features.Users.GetUserApplications;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Queries;

/// <summary>
/// Unit tests for GetUserApplicationsQueryHandler.
/// </summary>
public class GetUserApplicationsQueryHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IApplicationAccessRepository> _accessRepositoryMock = new();
    private readonly GetUserApplicationsQueryHandler _handler;

    public GetUserApplicationsQueryHandlerTests()
    {
        _handler = new GetUserApplicationsQueryHandler(
            _userRepositoryMock.Object,
            _accessRepositoryMock.Object,
            Mock.Of<IImageUrlComposer>(),
            new Mock<ILogger<GetUserApplicationsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_ReturnsApplicationsWithAccessSource()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _accessRepositoryMock
            .Setup(r => r.GetApplicationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new UserApplicationAccess(Guid.NewGuid(), "CMS", "CMS App", null, true, ViaOpenAccess: true, ViaGrant: false),
                new UserApplicationAccess(Guid.NewGuid(), "CRM", "CRM App", null, true, ViaOpenAccess: false, ViaGrant: true),
                new UserApplicationAccess(Guid.NewGuid(), "ERP", "ERP App", null, false, ViaOpenAccess: true, ViaGrant: true)
            ]);

        // Act
        var result = await _handler.Handle(new GetUserApplicationsQuery(userId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(3);
        result.Value.Single(a => a.Code == "CMS").AccessSource.Should().Be("open");
        result.Value.Single(a => a.Code == "CRM").AccessSource.Should().Be("grant");
        result.Value.Single(a => a.Code == "ERP").AccessSource.Should().Be("both");
    }

    [Fact]
    public async Task Handle_SortsByNameAscendingByDefault()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));

        _accessRepositoryMock
            .Setup(r => r.GetApplicationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new UserApplicationAccess(Guid.NewGuid(), "B", "Bravo", null, true, true, false),
                new UserApplicationAccess(Guid.NewGuid(), "A", "Alpha", null, true, true, false)
            ]);

        // Act
        var result = await _handler.Handle(new GetUserApplicationsQuery(userId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Select(a => a.Name).Should().ContainInOrder("Alpha", "Bravo");
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(new GetUserApplicationsQuery(userId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _accessRepositoryMock.Verify(
            r => r.GetApplicationsForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
