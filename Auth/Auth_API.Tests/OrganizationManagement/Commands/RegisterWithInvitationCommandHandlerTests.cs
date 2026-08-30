using Auth.Application.DTOs;
using Auth.Application.Features.Organizations.AcceptInvitation;
using Auth.Application.Features.Organizations.RegisterWithInvitation;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

/// <summary>
/// Unit tests for RegisterWithInvitationCommandHandler.
/// </summary>
public class RegisterWithInvitationCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly Mock<IRefreshTokenKeyService> _tokenKeyServiceMock = new();

    private readonly RegisterWithInvitationCommandHandler _handler;

    private const string Token = "aW52aXRlLXJlZ2lzdGVyLXRva2VuLWxvbmctZW5vdWdo";

    public RegisterWithInvitationCommandHandlerTests()
    {
        // Identity hash - see the note in AcceptInvitationCommandHandlerTests.
        _tokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns<string>(token => token);

        var passwordSettings = TestHelpers.CreatePasswordSettings();
        var passwordValidator = new PasswordValidator(TestHelpers.CreateOptions(passwordSettings));

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<string>()))
            .Returns("hashed-password");

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AcceptInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationAcceptResultDto
            {
                Success = true,
                OrganizationName = "Test Org",
                RoleName = "Member",
                Message = "Successfully joined the organization."
            });

        _handler = new RegisterWithInvitationCommandHandler(
            _userRepoMock.Object,
            _orgRepoMock.Object,
            _passwordHasherMock.Object,
            passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            TestHelpers.CreatePassingReservationGuard(),
            _mediatorMock.Object,
            _eventDispatcherMock.Object,
            _tokenKeyServiceMock.Object,
            new Mock<ILogger<RegisterWithInvitationCommandHandler>>().Object);
    }

    private static RegisterWithInvitationCommand CreateCommand(string password = "ValidPass1!")
        => new(Token, password, "Jane", "Doe");

    private OrganizationInvitation SetupInvitation(
        InvitationStatus status = InvitationStatus.Pending,
        DateTime? expiresAt = null,
        bool orgActive = true,
        bool userExists = false)
    {
        var orgId = Guid.NewGuid();
        var invitation = TestHelpers.CreateOrganizationInvitation(
            organizationId: orgId,
            email: "invitee@example.com",
            token: Token,
            status: status,
            expiresAt: expiresAt);
        var org = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: orgActive);

        _orgRepoMock.Setup(r => r.GetInvitationByTokenHashAsync(Token, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _userRepoMock
            .Setup(r => r.ExistsByEmailAsync("invitee@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userExists);

        return invitation;
    }

    [Fact]
    public async Task Handle_ValidData_CreatesConfirmedUserAndAcceptsInvitation()
    {
        SetupInvitation();

        User? createdUser = null;
        _userRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => createdUser = u);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Email.Should().Be("invitee@example.com");
        result.Value.OrganizationName.Should().Be("Test Org");
        result.Value.RoleName.Should().Be("Member");

        createdUser.Should().NotBeNull();
        createdUser!.Email.Value.Should().Be("invitee@example.com");
        createdUser.EmailConfirmed.Should().BeTrue("token possession proves mailbox ownership");

        _mediatorMock.Verify(m => m.Send(
            It.Is<AcceptInvitationCommand>(c => c.Token == Token && c.AcceptedBy == createdUser.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingUserWithInvitedEmail_ReturnsDuplicateEmailError()
    {
        SetupInvitation(userExists: true);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownToken_ReturnsNotFound()
    {
        _orgRepoMock
            .Setup(r => r.GetInvitationByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationInvitation?)null);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ExpiredInvitation_ReturnsErrorAndMarksExpired()
    {
        var invitation = SetupInvitation(expiresAt: DateTime.UtcNow.AddDays(-1));

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationExpired");
        invitation.Status.Should().Be(InvitationStatus.Expired);
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(InvitationStatus.Accepted)]
    [InlineData(InvitationStatus.Declined)]
    [InlineData(InvitationStatus.Cancelled)]
    public async Task Handle_NonPendingInvitation_ReturnsErrorWithoutCreatingUser(InvitationStatus status)
    {
        SetupInvitation(status: status);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveOrganization_ReturnsErrorWithoutCreatingUser()
    {
        SetupInvitation(orgActive: false);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.Inactive");
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WeakPassword_ReturnsValidationErrorWithoutCreatingUser()
    {
        SetupInvitation();

        var result = await _handler.Handle(CreateCommand(password: "weak"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AcceptFails_ReturnsAcceptErrors()
    {
        SetupInvitation();
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AcceptInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Conflict(code: "Organization.InvitationAlreadyAccepted", description: "Already accepted."));

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationAlreadyAccepted");
        // The user account was still created and can sign in to accept manually
        _userRepoMock.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
