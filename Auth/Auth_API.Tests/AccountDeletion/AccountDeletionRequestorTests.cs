using Auth.Application.Configuration;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.Users.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// Unit tests for the shared deletion-request pipeline: guards, single active
/// request, immediate deactivation + full revocation, and the requested event.
/// </summary>
public class AccountDeletionRequestorTests
{
    private readonly Mock<IAccountDeletionRequestRepository> _requestRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly AccountDeletionRequestor _requestor;

    public AccountDeletionRequestorTests()
    {
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());
        _requestRepositoryMock
            .Setup(r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _requestor = new AccountDeletionRequestor(
            _requestRepositoryMock.Object,
            _userRepositoryMock.Object,
            new OwnedOrganizationDeletionGuard(
                _organizationRepositoryMock.Object,
                new Mock<ILogger<OwnedOrganizationDeletionGuard>>().Object),
            _credentialRevocationMock.Object,
            _publisherMock.Object,
            TestHelpers.CreateOptions(new AccountDeletionSettings()),
            new Mock<ILogger<AccountDeletionRequestor>>().Object);
    }

    [Fact]
    public async Task RequestAsync_ValidUser_DeactivatesRevokesAndPublishes()
    {
        var user = TestHelpers.CreateUser();

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.InApp, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Status.Should().Be(AccountDeletionStatus.PendingGrace);
        result.Value.GraceEndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        _userRepositoryMock.Verify(r => r.DeleteAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _credentialRevocationMock.Verify(
            s => s.RevokeAllCredentialsAsync(user.Id, user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<AccountDeletionRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestAsync_SystemAccountId_IsRefusedEvenWithoutTheFlag()
    {
        var user = TestHelpers.CreateUser(id: WellKnownUserIds.System, isSystemUser: false);

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.InApp, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.CannotDeleteSystemUser);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestAsync_ActiveRequestExists_ReturnsAlreadyRequested()
    {
        var user = TestHelpers.CreateUser();
        _requestRepositoryMock
            .Setup(r => r.GetActiveByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountDeletionRequest.Create(
                user.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", user.Id));

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.InApp, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.DeletionAlreadyRequested);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestAsync_LosesInsertRace_ReturnsAlreadyRequested()
    {
        var user = TestHelpers.CreateUser();
        _requestRepositoryMock
            .Setup(r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.PublicWeb, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.DeletionAlreadyRequested);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestAsync_OwnedOrganizationWithMembers_BlocksWithoutSideEffects()
    {
        var user = TestHelpers.CreateUser();
        var organization = TestHelpers.CreateOrganization(ownerId: user.Id);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { organization });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [organization.Id] = 2 });

        var result = await _requestor.RequestAsync(user, AccountDeletionSource.InApp, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.CannotDeleteOrganizationOwner);
        _requestRepositoryMock.Verify(
            r => r.TryCreateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _credentialRevocationMock.Verify(
            s => s.RevokeAllCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
