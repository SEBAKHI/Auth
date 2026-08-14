using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Register;
using Auth.Application.Features.Authentication.SendEmailVerification;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

// Alias to use in mock return type
using SendVerificationResponse = Auth.Application.Features.Authentication.SendEmailVerification.SendEmailVerificationResponse;

namespace Auth_API.Tests.Authentication.Commands;

public class RegisterCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IPersonalOrganizationCreator> _personalOrgCreatorMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<RegisterCommandHandler>> _loggerMock;
    private readonly PasswordValidator _passwordValidator;
    private readonly RegisterCommandHandler _handler;

    public RegisterCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _personalOrgCreatorMock = new Mock<IPersonalOrganizationCreator>();
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<RegisterCommandHandler>>();

        var passwordSettings = TestHelpers.CreatePasswordSettings();
        _passwordValidator = new PasswordValidator(TestHelpers.CreateOptions(passwordSettings));

        _handler = new RegisterCommandHandler(
            _userRepositoryMock.Object,
            _passwordHasherMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            TestHelpers.CreatePassingReservationGuard(),
            _personalOrgCreatorMock.Object,
            _mediatorMock.Object,
            _loggerMock.Object);
    }

    private static RegisterCommand CreateCommand(
        string email = "test@example.com",
        string password = "ValidPass1!",
        string firstName = "Test",
        string lastName = "User",
        string? phoneNumber = null,
        string? preferredLanguage = null,
        string? timeZone = null,
        bool createOrganization = false)
        => new(email, password, firstName, lastName, phoneNumber, preferredLanguage, timeZone, createOrganization);

    [Fact]
    public async Task Handle_ValidData_ReturnsRegisterResponse()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendVerificationResponse(DateTime.UtcNow.AddMinutes(15), "t***t@example.com"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.UserId.Should().NotBeEmpty();
        result.Value.Message.Should().Contain("Registration successful");
        result.Value.OrganizationCreated.Should().BeFalse();
        // Expiry is surfaced so the verify screen can show a countdown without
        // requesting a fresh code.
        result.Value.VerificationCodeExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ReturnsDuplicateEmailError()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Handle_WeakPassword_ReturnsValidationError()
    {
        // Arrange
        var command = CreateCommand(password: "weak");
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidData_HashesPassword()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendVerificationResponse(DateTime.UtcNow.AddMinutes(15), "t***t@example.com"));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordHasherMock.Verify(h => h.HashPassword(command.Password), Times.Once());
    }

    [Fact]
    public async Task Handle_ValidData_PersistsUser()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendVerificationResponse(DateTime.UtcNow.AddMinutes(15), "t***t@example.com"));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<User>(u => u.Email == command.Email), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_WithCreateOrganization_CreatesPersonalOrganization()
    {
        // Arrange
        var command = CreateCommand(createOrganization: true);
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _personalOrgCreatorMock
            .Setup(p => p.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendVerificationResponse(DateTime.UtcNow.AddMinutes(15), "t***t@example.com"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.OrganizationCreated.Should().BeTrue();
        _personalOrgCreatorMock.Verify(
            p => p.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_WithoutCreateOrganization_DoesNotCreateOrganization()
    {
        // Arrange
        var command = CreateCommand(createOrganization: false);
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendVerificationResponse(DateTime.UtcNow.AddMinutes(15), "t***t@example.com"));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _personalOrgCreatorMock.Verify(
            p => p.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_ValidData_SendsVerificationEmail()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendVerificationResponse(DateTime.UtcNow.AddMinutes(15), "t***t@example.com"));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_VerificationEmailFails_StillReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand();
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<SendVerificationResponse>)Error.Failure("Email.SendFailed", "Email sending failed"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.UserId.Should().NotBeEmpty();
        // A failed send leaves no expiry to advertise.
        result.Value.VerificationCodeExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithCustomLanguageAndTimezone_CreatesUserWithCustomValues()
    {
        // Arrange
        var command = CreateCommand(preferredLanguage: "ar", timeZone: "Asia/Riyadh");
        _userRepositoryMock
            .Setup(r => r.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("hashed-password");
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SendEmailVerificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendVerificationResponse(DateTime.UtcNow.AddMinutes(15), "t***t@example.com"));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userRepositoryMock.Verify(
            r => r.CreateAsync(
                It.Is<User>(u => u.PreferredLanguage == "ar" && u.TimeZone == "Asia/Riyadh"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
