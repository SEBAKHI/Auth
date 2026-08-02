using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Features.Applications.DeleteApplication;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement.Commands;

/// <summary>
/// Base URL the image composer is configured with in these tests, so logo
/// normalization (composed URL in, storage key stored) is exercised for real.
/// </summary>
internal static class ApplicationTestImages
{
    public const string PublicBaseUrl = "https://cdn.example.com/images";

    public static ImageUrlComposer Composer() => new(TestHelpers.CreateOptions(
        new ImageStorageSettings { PublicBaseUrl = PublicBaseUrl }));
}

/// <summary>
/// Unit tests for CreateApplicationCommandHandler.
/// </summary>
public class CreateApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<CreateApplicationCommandHandler>> _loggerMock;
    private readonly CreateApplicationCommandHandler _handler;

    public CreateApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<CreateApplicationCommandHandler>>();

        _handler = new CreateApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            ApplicationTestImages.Composer(),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_CreatesAndReturnsDto()
    {
        // Arrange
        var createdBy = Guid.NewGuid();
        var command = new CreateApplicationCommand(
            Code: "CRM",
            Name: "CRM Application",
            Description: "Customer Relationship Management",
            BaseUrl: "https://crm.example.com")
        { CreatedBy = createdBy };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Code.Should().Be("CRM");
        result.Value.Name.Should().Be("CRM Application");
        result.Value.Description.Should().Be("Customer Relationship Management");
        result.Value.BaseUrl.Should().Be("https://crm.example.com");
        result.Value.IsActive.Should().BeTrue();

        _applicationRepositoryMock.Verify(
            r => r.CreateAsync(It.Is<ApplicationEntity>(a =>
                a.Code == "CRM" &&
                a.Name == "CRM Application"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithRedirectUris_RegistersAllowlistAtCreation()
    {
        // Regression: the create path used to ignore redirect URIs entirely, so
        // a new OAuth client had to be created and then edited before it could
        // complete a single authorization request.
        var command = new CreateApplicationCommand(
            Code: "PORTAL",
            Name: "Portal",
            RedirectUris: ["https://portal.example.com/callback", " https://portal.example.com/callback ", "http://localhost:3000/api/auth/callback"],
            ReauthenticationMaxAgeMinutes: 45)
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ApplicationEntity? persisted = null;
        _applicationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => persisted = app);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — trimmed and de-duplicated by the entity, and persisted with
        // the application rather than left for a follow-up update.
        result.IsError.Should().BeFalse();
        result.Value.RedirectUris.Should().Equal(
            "https://portal.example.com/callback",
            "http://localhost:3000/api/auth/callback");
        result.Value.ReauthenticationMaxAgeMinutes.Should().Be(45);

        persisted.Should().NotBeNull();
        persisted!.RedirectUris.Should().Equal(
            "https://portal.example.com/callback",
            "http://localhost:3000/api/auth/callback");
        persisted.ReauthenticationMaxAgeMinutes.Should().Be(45);
    }

    [Fact]
    public async Task Handle_WithoutRedirectUris_LeavesAllowlistEmpty()
    {
        var command = new CreateApplicationCommand(Code: "CRM", Name: "CRM")
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUris.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ComposedLogoUrl_StoresTheStorageKey()
    {
        var command = new CreateApplicationCommand(
            Code: "CRM",
            Name: "CRM",
            LogoUrl: $"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp")
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ApplicationEntity? persisted = null;
        _applicationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => persisted = app);

        var result = await _handler.Handle(command, CancellationToken.None);

        persisted!.LogoUrl.Should().Be("apps/crm.webp");
        result.Value.LogoUrl.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp");
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsConflictError()
    {
        // Arrange
        var command = new CreateApplicationCommand(
            Code: "AUTH",
            Name: "Duplicate App")
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Application.DuplicateCode");

        _applicationRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for UpdateApplicationCommandHandler.
/// </summary>
public class UpdateApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<UpdateApplicationCommandHandler>> _loggerMock;
    private readonly UpdateApplicationCommandHandler _handler;

    public UpdateApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<UpdateApplicationCommandHandler>>();

        _handler = new UpdateApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            ApplicationTestImages.Composer(),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidData_UpdatesAndReturnsDto()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var modifiedBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "Old Name");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "Updated CRM",
            Description: "Updated description",
            BaseUrl: "https://crm-v2.example.com",
            LogoUrl: "https://logo.example.com/crm.png",
            ContactEmail: "crm@example.com",
            AllowSelfRegistration: true,
            RequireTwoFactor: true,
            RequireEmailVerification: true,
            SessionTimeoutMinutes: 120,
            MaxConcurrentSessions: 10)
        { ModifiedBy = modifiedBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(appId);
        result.Value.Code.Should().Be("CRM");
        result.Value.Name.Should().Be("Updated CRM");
        result.Value.Description.Should().Be("Updated description");
        result.Value.BaseUrl.Should().Be("https://crm-v2.example.com");
        result.Value.LogoUrl.Should().Be("https://logo.example.com/crm.png");
        result.Value.ContactEmail.Should().Be("crm@example.com");
        result.Value.AllowSelfRegistration.Should().BeTrue();
        result.Value.RequireTwoFactor.Should().BeTrue();
        result.Value.RequireEmailVerification.Should().BeTrue();
        result.Value.SessionTimeoutMinutes.Should().Be(120);
        result.Value.MaxConcurrentSessions.Should().Be(10);

        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // The update contract is a full replace except for the allowlist: null means
    // "the caller is not managing redirect URIs", an empty list means "clear
    // them". A caller that builds a partial body must send null, not [] — [] is
    // how an application loses every redirect URI it had.
    private const string ExistingRedirectUri = "https://crm.example.com/callback";

    [Fact]
    public async Task Handle_NullRedirectUris_LeavesTheAllowlistUntouched()
    {
        var result = await UpdateWithRedirectUris(null);

        result.Value.RedirectUris.Should().Equal(ExistingRedirectUri);
    }

    [Fact]
    public async Task Handle_EmptyRedirectUris_ClearsTheAllowlist()
    {
        var result = await UpdateWithRedirectUris([]);

        result.Value.RedirectUris.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NewRedirectUris_ReplacesTheAllowlist()
    {
        var result = await UpdateWithRedirectUris(["https://new.example.com/callback"]);

        result.Value.RedirectUris.Should().Equal("https://new.example.com/callback");
    }

    private async Task<ErrorOr<ApplicationDto>> UpdateWithRedirectUris(IReadOnlyList<string>? submitted)
    {
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM");
        application.LoadRedirectUris([ExistingRedirectUri]);

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "CRM",
            RedirectUris: submitted)
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        return result;
    }

    [Fact]
    public async Task Handle_ComposedLogoUrl_StoresTheStorageKeyNotTheAbsoluteUrl()
    {
        // The console resends the absolute URL it last read. Storing that would
        // bind the row to the current image host, so it is normalized back to a
        // key on the way in and composed again on the way out.
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "CRM",
            LogoUrl: $"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp")
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var result = await _handler.Handle(command, CancellationToken.None);

        application.LogoUrl.Should().Be("apps/crm.webp");
        result.Value.LogoUrl.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp");
    }

    [Fact]
    public async Task Handle_ExternalLogoUrl_IsStoredUnchanged()
    {
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "CRM",
            LogoUrl: "https://other-host.example.com/logo.svg")
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var result = await _handler.Handle(command, CancellationToken.None);

        application.LogoUrl.Should().Be("https://other-host.example.com/logo.svg");
        result.Value.LogoUrl.Should().Be("https://other-host.example.com/logo.svg");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "Updated Name")
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for DeleteApplicationCommandHandler.
/// </summary>
public class DeleteApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<DeleteApplicationCommandHandler>> _loggerMock;
    private readonly DeleteApplicationCommandHandler _handler;

    public DeleteApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<DeleteApplicationCommandHandler>>();

        _handler = new DeleteApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidApplication_SoftDeletesWithActingUser()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var deletedBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");

        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = deletedBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveUserAssignmentsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveOrganizationsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.DeleteAsync(appId, deletedBy, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeTrue();

        // The acting user (not the application id) must be recorded as DeletedBy.
        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(appId, deletedBy, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AnyCodeIncludingAuth_DeletesSuccessfully()
    {
        // Regression for the retired "system application" guard: deletion is
        // decided by Id and dependency state only — never by name or code.
        // Uses the exact lowercase code the old case-sensitive guard missed.
        var appId = Guid.NewGuid();
        var deletedBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "auth", name: "Auth System");

        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = deletedBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(appId, deletedBy, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Application.NotFound");

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveUserAssignments_ReturnsConflict()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");
        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveUserAssignmentsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Application.HasActiveUsers");

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveOrganizations_ReturnsConflict()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM App");
        var command = new DeleteApplicationCommand(Id: appId) { DeletedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveUserAssignmentsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _applicationRepositoryMock
            .Setup(r => r.HasActiveOrganizationsAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        result.FirstError.Code.Should().Be("Application.HasActiveOrganizations");

        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
