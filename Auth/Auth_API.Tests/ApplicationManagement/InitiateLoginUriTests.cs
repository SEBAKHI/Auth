using Auth.Application.DTOs;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.GetApplicationById;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.ErrorContract;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// OI-192: an application's initiate-login URI, the address in the application that starts
/// its own sign-in, where an invitee is sent after accepting. The browser lands there, so
/// the scheme rule is the redirect URIs' one; create, update and get carry it, and null on
/// update leaves it as it is (the console's logo uploads re-send the body without it).
/// </summary>
public class InitiateLoginUriTests
{
    private const string Start = "https://crm.example.com/signin/start";

    // --- validation: what is accepted, and each refusal with its code ---

    public static TheoryData<string> AcceptedUris => new()
    {
        Start,
        "https://crm.example.com/signin/start?intent=signin",
        "http://localhost:5173/login",
        "http://127.0.0.1:8080/login",
        "http://[::1]:8080/login",
        "  https://crm.example.com/start  ",   // padded input is trimmed
        "https://crm.example.com/" + new string('a', 476), // exactly 500 characters
    };

    [Fact]
    public void AcceptedUris_HasCases() => AcceptedUris.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(AcceptedUris))]
    public void Validate_CreateAndUpdateWithAnAcceptedUri_AreValid(string uri)
    {
        uri.Trim().Length.Should().BeLessThanOrEqualTo(500);

        new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("CRM", "CRM", InitiateLoginUri: uri)).IsValid.Should().BeTrue();
        new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "CRM", InitiateLoginUri: uri)).IsValid.Should().BeTrue();
    }

    public static TheoryData<string, string> RefusedUris => new()
    {
        { "http://crm.example.com/signin/start", ApplicationErrors.InitiateLoginUriInvalid.Code },   // http off loopback
        { "http://127.0.0.2/login", ApplicationErrors.InitiateLoginUriInvalid.Code },             // not one of the three hosts
        { "/signin/start", ApplicationErrors.InitiateLoginUriInvalid.Code },                         // relative
        { "crm.example.com/signin/start", ApplicationErrors.InitiateLoginUriInvalid.Code },          // no scheme
        { Start + "#signin", ApplicationErrors.InitiateLoginUriInvalid.Code },                    // fragment
        { Start + "#", ApplicationErrors.InitiateLoginUriInvalid.Code },                          // an empty fragment is still one
        { "https://user:secret@crm.example.com/login", ApplicationErrors.InitiateLoginUriInvalid.Code }, // userinfo
        { "https://crm.example.com@evil.example.com/login", ApplicationErrors.InitiateLoginUriInvalid.Code }, // userinfo posing as the host
        { "javascript:alert(1)", ApplicationErrors.InitiateLoginUriInvalid.Code },
        { "ftp://crm.example.com/login", ApplicationErrors.InitiateLoginUriInvalid.Code },
        { "https://crm.example.com/" + new string('a', 477), ApplicationErrors.InitiateLoginUriTooLong.Code }, // 501 characters
    };

    [Fact]
    public void RefusedUris_HasCases() => RefusedUris.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(RefusedUris))]
    public void Validate_CreateAndUpdateWithARefusedUri_FailWithItsCode(string uri, string code)
    {
        var create = new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("CRM", "CRM", InitiateLoginUri: uri));
        var update = new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "CRM", InitiateLoginUri: uri));

        foreach (var result in new[] { create, update })
        {
            var failure = Assert.Single(result.Errors);
            failure.ErrorCode.Should().Be(code);
            failure.PropertyName.Should().Be(nameof(CreateApplicationCommand.InitiateLoginUri));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NoUri_IsValid(string? uri)
    {
        // Blank means none on create, and removes it on update.
        new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("CRM", "CRM", InitiateLoginUri: uri)).IsValid.Should().BeTrue();
        new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "CRM", InitiateLoginUri: uri)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Application.InitiateLoginUriInvalid")]
    [InlineData("Application.InitiateLoginUriTooLong")]
    public void TheCodes_ArePublishedAs400WithThePointerToTheField(string code)
    {
        var published = PublishedErrorCodes.All[code];

        published.Status.Should().Be(400);
        published.Source.Should().Be("catalog");
        published.Pointer.Should().Be("#/initiateLoginUri");
    }

    // --- the handlers: create, update and get carry it ---

    [Fact]
    public async Task Handle_CreateWithAUri_RegistersAndReturnsIt()
    {
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.ExistsByCodeAsync("CRM", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ApplicationEntity? persisted = null;
        repository
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => persisted = app);

        var result = await CreateHandler(repository).Handle(
            new CreateApplicationCommand("CRM", "CRM", InitiateLoginUri: $" {Start} ") { CreatedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.InitiateLoginUri.Should().Be(Start);
        persisted!.InitiateLoginUri.Should().Be(Start);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_CreateWithoutAUri_RegistersNone(string? uri)
    {
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.ExistsByCodeAsync("CRM", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);

        var result = await CreateHandler(repository).Handle(
            new CreateApplicationCommand("CRM", "CRM", InitiateLoginUri: uri) { CreatedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.Value.InitiateLoginUri.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UpdateWithAUri_ReplacesIt()
    {
        var (result, saved) = await UpdateWith("https://crm.example.com/start");

        result.Value.InitiateLoginUri.Should().Be("https://crm.example.com/start");
        saved.InitiateLoginUri.Should().Be("https://crm.example.com/start");
    }

    [Fact]
    public async Task Handle_UpdateWithoutTheField_LeavesItAsItIs()
    {
        // The console's logo uploads re-send the body without it: they must not erase it.
        var (result, saved) = await UpdateWith(null);

        result.Value.InitiateLoginUri.Should().Be(Start);
        saved.InitiateLoginUri.Should().Be(Start);
    }

    [Fact]
    public async Task Handle_UpdateWithTheEmptyString_RemovesIt()
    {
        var (result, saved) = await UpdateWith("");

        result.Value.InitiateLoginUri.Should().BeNull();
        saved.InitiateLoginUri.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Get_ReturnsIt()
    {
        var id = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: id, code: "CRM");
        application.LoadInitiateLoginUri(Start);
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(application);

        var handler = new GetApplicationByIdQueryHandler(
            repository.Object,
            new Mock<IUserRepository>().Object,
            ApplicationTestImages.Composer(),
            new Mock<ILogger<GetApplicationByIdQueryHandler>>().Object);

        var result = await handler.Handle(new GetApplicationByIdQuery(id), CancellationToken.None);

        result.Value.InitiateLoginUri.Should().Be(Start);
    }

    private static CreateApplicationCommandHandler CreateHandler(Mock<IApplicationRepository> repository) =>
        new(
            repository.Object,
            ApplicationTestImages.Composer(),
            ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
            Mock.Of<MediatR.IPublisher>(),
            new Mock<ILogger<CreateApplicationCommandHandler>>().Object);

    private static async Task<(ErrorOr<ApplicationDto> Result, ApplicationEntity Saved)> UpdateWith(string? submitted)
    {
        var id = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: id, code: "CRM", name: "CRM");
        application.LoadInitiateLoginUri(Start);

        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(application);
        var permissions = new Mock<IPermissionRepository>();

        var handler = new UpdateApplicationCommandHandler(
            repository.Object,
            new Mock<ICredentialRevocationService>().Object,
            ApplicationTestImages.Composer(),
            new Auth.Application.Common.OrganizationCreatorRoleCheck(new Mock<IRoleRepository>().Object, permissions.Object),
            new Auth.Application.Common.PermissionGrantGuard(permissions.Object),
            ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
            Mock.Of<MediatR.IPublisher>(),
            new Mock<ILogger<UpdateApplicationCommandHandler>>().Object);

        var result = await handler.Handle(
            new UpdateApplicationCommand(id, "CRM", InitiateLoginUri: submitted) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        repository.Verify(r => r.UpdateAsync(application, It.IsAny<CancellationToken>()), Times.Once);
        return (result, application);
    }
}
