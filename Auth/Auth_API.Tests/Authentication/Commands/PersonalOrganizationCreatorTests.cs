using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth_API.Tests.Helpers;
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
        // Open: the failure tests below are about what happens once creation runs.
        _creator = Creator(new OrganizationSettings { AllowSelfServiceCreation = true });
    }

    private PersonalOrganizationCreator Creator(OrganizationSettings settings) =>
        new(
            _organizations.Object,
            _roles.Object,
            TestHelpers.CreateOptions(settings),
            new Mock<ILogger<PersonalOrganizationCreator>>().Object);

    private void OwnerRoleExists()
    {
        _roles
            .Setup(r => r.GetByCodeAsync((Guid?)null, "org-owner", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(code: "org-owner"));
        _organizations
            .Setup(o => o.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization org, CancellationToken _) => org);
        _organizations
            .Setup(o => o.AddMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser member, CancellationToken _) => member);
    }

    /// <summary>
    /// PR #47 review F2 (D-47-1): the creator becomes org-owner, invitation included,
    /// so with self-service creation closed — the shipped default — no organization
    /// is made, whatever the request asked.
    /// </summary>
    [Fact]
    public async Task WithShippedDefaults_CreatesNothing_AndAnswersNotCreated()
    {
        OwnerRoleExists();

        var created = await Creator(new OrganizationSettings())
            .CreateAsync(User.Create("jane@one.example", "hash", "Jane", "Doe", Guid.Empty), CancellationToken.None);

        created.Should().BeFalse();
        _organizations.Verify(o => o.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Never);
        _organizations.Verify(o => o.AddMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithSelfServiceOpen_CreatesTheOrganization()
    {
        OwnerRoleExists();

        var created = await _creator
            .CreateAsync(User.Create("jane@one.example", "hash", "Jane", "Doe", Guid.Empty), CancellationToken.None);

        created.Should().BeTrue();
        _organizations.Verify(o => o.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Once);
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
