using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The personal organization is a convenience created after the account row is
/// committed, by callers that cannot undo that row. Its failure is therefore
/// answered as "not created", never thrown.
/// </summary>
public class PersonalOrganizationCreatorTests
{
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly PersonalOrganizationCreator _creator;

    public PersonalOrganizationCreatorTests()
    {
        _creator = new PersonalOrganizationCreator(
            _organizations.Object,
            _roles.Object,
            new Mock<ILogger<PersonalOrganizationCreator>>().Object);
    }

    [Fact]
    public async Task AFailureInsideCreate_IsLoggedAndAnsweredAsNotCreated()
    {
        _roles
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("roles unavailable"));

        var created = await _creator.CreateAsync(User.Create("jane@one.example", "hash", "Jane", "Doe", Guid.Empty), CancellationToken.None);

        created.Should().BeFalse("the account exists; a 500 here would tell its owner it failed while their address was taken");
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        _roles
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _creator.CreateAsync(User.Create("jane@one.example", "hash", "Jane", "Doe", Guid.Empty), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AMissingOwnerRole_IsStillNotCreated_NotAThrow()
    {
        _roles
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        var created = await _creator.CreateAsync(User.Create("jane@one.example", "hash", "Jane", "Doe", Guid.Empty), CancellationToken.None);

        created.Should().BeFalse();
        _organizations.Verify(o => o.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
