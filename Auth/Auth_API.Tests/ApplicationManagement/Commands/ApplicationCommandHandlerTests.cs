using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Features.Applications.DeleteApplication;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
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

    /// <summary>
    /// The real logo rule over a mocked ledger: a key claims only what the mock
    /// is set up to grant, so a writer that skips the rule stores a key nobody
    /// granted and its test goes red.
    /// </summary>
    public static ImageReferenceGuard Guard(Mock<IUploadedImageRepository> uploadedImages) =>
        new(uploadedImages.Object, Composer());
}

/// <summary>
/// Unit tests for CreateApplicationCommandHandler.
/// </summary>
public class CreateApplicationCommandHandlerTests
{
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IUploadedImageRepository> _uploadedImagesMock = new();
    private readonly Mock<ILogger<CreateApplicationCommandHandler>> _loggerMock;
    private readonly CreateApplicationCommandHandler _handler;

    public CreateApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<CreateApplicationCommandHandler>>();

        _handler = new CreateApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            ApplicationTestImages.Composer(),
            ApplicationTestImages.Guard(_uploadedImagesMock),
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
        // No allowed scopes either: a new application is granted openid only.
        result.Value.AllowedScopes.Should().BeEmpty();
    }

    private async Task<(ErrorOr<ApplicationDto> Result, ApplicationEntity? Persisted)> CreateWithScopes(
        IReadOnlyList<string>? allowedScopes)
    {
        var command = new CreateApplicationCommand(Code: "EDIS", Name: "EDIS", AllowedScopes: allowedScopes)
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        ApplicationEntity? persisted = null;
        _applicationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => persisted = app);

        return (await _handler.Handle(command, CancellationToken.None), persisted);
    }

    [Fact]
    public async Task Handle_WithAllowedScopes_StoresThemCanonically()
    {
        var (result, persisted) = await CreateWithScopes(["phone", "email"]);

        result.IsError.Should().BeFalse();
        result.Value.AllowedScopes.Should().Equal("email", "phone");
        persisted!.AllowedScopes.OptionalValue.Should().Be("email phone");
    }

    [Fact]
    public async Task Handle_WithOnlyOpenId_StoresNothing()
    {
        // openid is every application's; naming it changes nothing (NULL stored).
        var (result, persisted) = await CreateWithScopes(["openid"]);

        result.Value.AllowedScopes.Should().BeEmpty();
        persisted!.AllowedScopes.OptionalValue.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithUnknownScope_ReturnsAllowedScopesInvalidAndCreatesNothing()
    {
        var (result, persisted) = await CreateWithScopes(["address"]);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(ApplicationErrors.AllowedScopesInvalid.Code);
        persisted.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ComposedLogoUrlOfOwnUpload_StoresTheStorageKeyAndClaimsIt()
    {
        var createdBy = Guid.NewGuid();
        var command = new CreateApplicationCommand(
            Code: "CRM",
            Name: "CRM",
            LogoUrl: $"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp")
        { CreatedBy = createdBy };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _uploadedImagesMock
            .Setup(r => r.TryClaimAsync("apps/crm.webp", createdBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ApplicationEntity? persisted = null;
        _applicationRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => persisted = app);

        var result = await _handler.Handle(command, CancellationToken.None);

        persisted!.LogoUrl.Should().Be("apps/crm.webp");
        result.Value.LogoUrl.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp");
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync("apps/crm.webp", createdBy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AnotherUsersUploadKey_ReturnsImageNotAvailableAndCreatesNothing()
    {
        // The ledger grants the key to nobody but its uploader (TryClaimAsync is
        // false for everyone else), so naming somebody else's key is refused
        // before anything is written.
        var command = new CreateApplicationCommand(
            Code: "CRM",
            Name: "CRM",
            LogoUrl: "someone-elses.webp")
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(ImageErrors.NotAvailable.Code);
        _applicationRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync("someone-elses.webp", command.CreatedBy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RefusedForAnotherReason_ClaimsNoLogo()
    {
        // The claim runs last before the write, so a request refused earlier
        // leaves no attached row behind.
        var command = new CreateApplicationCommand(
            Code: "CRM",
            Name: "CRM",
            LogoUrl: "own.webp",
            AllowOrganizationCreation: true)
        { CreatedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.ExistsByCodeAsync(command.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.FirstError.Code.Should().Be(ApplicationErrors.OrganizationCreatorRoleInvalid.Code);
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<ILogger<UpdateApplicationCommandHandler>> _loggerMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly Mock<IUploadedImageRepository> _uploadedImagesMock = new();
    private readonly UpdateApplicationCommandHandler _handler;

    public UpdateApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<UpdateApplicationCommandHandler>>();

        _handler = new UpdateApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            _credentialRevocationMock.Object,
            ApplicationTestImages.Composer(),
            new Auth.Application.Common.OrganizationCreatorRoleCheck(
                _roleRepositoryMock.Object, _permissionRepositoryMock.Object),
            new Auth.Application.Common.PermissionGrantGuard(_permissionRepositoryMock.Object),
            ApplicationTestImages.Guard(_uploadedImagesMock),
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

    // Allowed scopes follow the same rule, for the same reason: a console bundle
    // cached from before the field existed sends none, and renaming EDIS must
    // not silently strip its phone scope.
    private async Task<(ErrorOr<ApplicationDto> Result, ApplicationEntity Application)> UpdateWithScopes(
        IReadOnlyList<string>? submitted)
    {
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "EDIS", name: "EDIS");
        application.LoadAllowedScopes("profile email phone");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "EDIS renamed",
            AllowedScopes: submitted)
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        return (await _handler.Handle(command, CancellationToken.None), application);
    }

    [Fact]
    public async Task Handle_NullAllowedScopes_LeavesThemUnchanged()
    {
        var (result, application) = await UpdateWithScopes(null);

        result.IsError.Should().BeFalse();
        result.Value.Name.Should().Be("EDIS renamed");
        result.Value.AllowedScopes.Should().Equal("profile", "email", "phone");
        application.AllowedScopes.OptionalValue.Should().Be("profile email phone");
    }

    [Fact]
    public async Task Handle_EmptyAllowedScopes_ClearsThemToOpenIdOnly()
    {
        var (result, application) = await UpdateWithScopes([]);

        result.Value.AllowedScopes.Should().BeEmpty();
        application.AllowedScopes.OptionalValue.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NewAllowedScopes_ReplaceThem()
    {
        var (result, _) = await UpdateWithScopes(["phone"]);

        result.Value.AllowedScopes.Should().Equal("phone");
    }

    [Fact]
    public async Task Handle_UnknownAllowedScope_ReturnsAllowedScopesInvalidAndSavesNothing()
    {
        var (result, application) = await UpdateWithScopes(["phone_number"]);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(ApplicationErrors.AllowedScopesInvalid.Code);
        application.AllowedScopes.OptionalValue.Should().Be("profile email phone");
        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task Handle_ComposedLogoUrlOfOwnUpload_StoresTheStorageKeyAndClaimsIt()
    {
        // The console resends the absolute URL it last read. Storing that would
        // bind the row to the current image host, so it is normalized back to a
        // key on the way in and composed again on the way out.
        var appId = Guid.NewGuid();
        var modifiedBy = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "CRM",
            LogoUrl: $"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp")
        { ModifiedBy = modifiedBy };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _uploadedImagesMock
            .Setup(r => r.TryClaimAsync("apps/crm.webp", modifiedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(command, CancellationToken.None);

        application.LogoUrl.Should().Be("apps/crm.webp");
        result.Value.LogoUrl.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/apps/crm.webp");
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync("apps/crm.webp", modifiedBy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AnotherUsersUploadKey_ReturnsImageNotAvailableAndSavesNothing()
    {
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: appId, code: "CRM", name: "CRM");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "CRM",
            LogoUrl: "someone-elses.webp")
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(ImageErrors.NotAvailable.Code);
        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync("someone-elses.webp", command.ModifiedBy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnchangedLogoResentComposed_SavesWithoutAClaim()
    {
        // Editing the name of an application whose logo predates the ledger, or
        // was uploaded by another administrator, must not fail on the logo.
        var appId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(
            id: appId, code: "CRM", name: "CRM", logoUrl: "legacy-logo.webp");

        var command = new UpdateApplicationCommand(
            Id: appId,
            Name: "CRM renamed",
            LogoUrl: $"{ApplicationTestImages.PublicBaseUrl}/legacy-logo.webp")
        { ModifiedBy = Guid.NewGuid() };

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        application.LogoUrl.Should().Be("legacy-logo.webp");
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _applicationRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Once);
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
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<ILogger<DeleteApplicationCommandHandler>> _loggerMock;
    private readonly DeleteApplicationCommandHandler _handler;

    public DeleteApplicationCommandHandlerTests()
    {
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<DeleteApplicationCommandHandler>>();

        _handler = new DeleteApplicationCommandHandler(
            _applicationRepositoryMock.Object,
            _credentialRevocationMock.Object,
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

    private (ApplicationEntity Application, DeleteApplicationCommand Command) SetupDeletable()
    {
        var application = TestHelpers.CreateApplication(code: "CRM");
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _applicationRepositoryMock
            .Setup(r => r.HasActiveUserAssignmentsAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _applicationRepositoryMock
            .Setup(r => r.HasActiveOrganizationsAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return (application, new DeleteApplicationCommand(Id: application.Id) { DeletedBy = Guid.NewGuid() });
    }

    [Fact]
    public async Task Handle_Delete_KillsTheApplicationsSessionsBeforeDeletingTheRow()
    {
        // Deletion used to revoke nothing: the application's tokens kept reading
        // UserInfo until they expired. And a deleted application can no longer be
        // looked up, so the revocation must come first or it could never be retried.
        var (application, command) = SetupDeletable();
        var order = new List<string>();
        _credentialRevocationMock
            .Setup(c => c.TerminateApplicationSessionsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("sessions killed"))
            .ReturnsAsync(3);
        _applicationRepositoryMock
            .Setup(r => r.DeleteAsync(application.Id, command.DeletedBy, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("row deleted"))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        order.Should().Equal("sessions killed", "row deleted");
        _credentialRevocationMock.Verify(
            c => c.TerminateApplicationSessionsAsync(
                application.Id, null, command.DeletedBy,
                TokenRevocationReasons.ApplicationDeleted, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Delete_WhenTheRevocationFails_LeavesTheApplicationInPlace()
    {
        // A failure must leave something the operator can retry: the application.
        var (application, command) = SetupDeletable();
        _credentialRevocationMock
            .Setup(c => c.TerminateApplicationSessionsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _applicationRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Delete_FromTheFirstWriteOn_DoesNotDependOnTheCaller()
    {
        var (application, command) = SetupDeletable();
        using var cancellation = new CancellationTokenSource();
        var tokens = new List<CancellationToken>();
        _credentialRevocationMock
            .Setup(c => c.TerminateApplicationSessionsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid? _, Guid? _, string _, CancellationToken ct) =>
            {
                tokens.Add(ct);
                cancellation.Cancel(); // the caller hangs up right after the first write
            })
            .ReturnsAsync(1);
        _applicationRepositoryMock
            .Setup(r => r.DeleteAsync(application.Id, command.DeletedBy, It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, CancellationToken ct) => tokens.Add(ct))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(command, cancellation.Token);

        result.IsError.Should().BeFalse();
        tokens.Should().HaveCount(2).And.OnlyContain(ct => !ct.CanBeCanceled);
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
