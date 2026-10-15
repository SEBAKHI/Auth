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
/// OI-151: an application's post-logout redirect URIs, the allowlist the
/// end-session endpoint returns the browser to. The same rules as the redirect
/// URIs, its own list and its own codes: create, update and get carry it, null on
/// update leaves it as it is, and a refused entry names this list, not the other.
/// </summary>
public class PostLogoutRedirectUrisTests
{
    private const string Return = "https://shop.example.com/signed-out";
    private const string LocalReturn = "http://localhost:3000/signed-out";

    // --- the entity: exact match, its own list ---

    [Fact]
    public void IsPostLogoutRedirectUriAllowed_MatchesExactlyAndNothingElse()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetPostLogoutRedirectUris([$" {Return} ", Return, LocalReturn], Guid.NewGuid());

        application.PostLogoutRedirectUris.Should().Equal(Return, LocalReturn);
        application.IsPostLogoutRedirectUriAllowed(Return).Should().BeTrue();
        application.IsPostLogoutRedirectUriAllowed(LocalReturn).Should().BeTrue();

        application.IsPostLogoutRedirectUriAllowed("https://SHOP.example.com/signed-out").Should().BeFalse();
        application.IsPostLogoutRedirectUriAllowed(Return + "/").Should().BeFalse();
        application.IsPostLogoutRedirectUriAllowed(Return + "?x=1").Should().BeFalse();
        application.IsPostLogoutRedirectUriAllowed("https://shop.example.com").Should().BeFalse();
    }

    [Fact]
    public void TheTwoLists_AreSeparatePermissions()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetRedirectUris(["https://shop.example.com/callback"], Guid.NewGuid());

        application.IsPostLogoutRedirectUriAllowed("https://shop.example.com/callback").Should().BeFalse(
            "a sign-in return is not a sign-out return");

        application.SetPostLogoutRedirectUris([Return], Guid.NewGuid());
        application.IsRedirectUriAllowed(Return).Should().BeFalse();
        application.RedirectUris.Should().Equal("https://shop.example.com/callback");
    }

    // --- validation: the redirect-URI rules, its own codes ---

    public static TheoryData<string> InvalidReturns => new()
    {
        "http://shop.example.com/signed-out",       // http off localhost
        "/signed-out",                              // relative
        "shop.example.com/signed-out",              // no scheme
        Return + "#done",                           // fragment
        "https://shop.example.com/" + new string('a', 476), // 501 characters
        "javascript:alert(1)",
        "",
        // Printable ASCII only (0x21-0x7E): the address goes as-is into a
        // Location header, which refuses anything else with a 500.
        "https://shop.example.com/out\r\nX-A: b",  // CR LF: a header injection attempt
        "https://shop.example.com/a b",             // a space
        "https://shop.example.com/\u00e9",          // non-ASCII
        "https://shop.example.com/a\tb",            // a TAB inside
    };

    [Fact]
    public void InvalidReturns_HasCases() => InvalidReturns.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(InvalidReturns))]
    public void Validate_CreateAndUpdateWithAnInvalidReturn_FailWithPostLogoutRedirectUriInvalid(string uri)
    {
        var create = new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("SHOP", "SHOP", PostLogoutRedirectUris: [Return, uri]));
        var update = new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "SHOP", PostLogoutRedirectUris: [Return, uri]));

        foreach (var result in new[] { create, update })
        {
            var failure = Assert.Single(result.Errors);
            failure.ErrorCode.Should().Be(ApplicationErrors.PostLogoutRedirectUriInvalid.Code);
            failure.PropertyName.Should().StartWith(nameof(CreateApplicationCommand.PostLogoutRedirectUris));
        }
    }

    public static TheoryData<string> AcceptedReturns => new()
    {
        "https://shop.example.com/%C3%A9",   // percent-encoded stays allowed
        "  https://shop.example.com/out  ",  // padded input is trimmed, as before
        "http://localhost:3000/signed-out",
    };

    [Fact]
    public void AcceptedReturns_HasCases() => AcceptedReturns.Count.Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(AcceptedReturns))]
    public void Validate_CreateAndUpdateWithAnAcceptedReturn_AreValid(string uri)
    {
        new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("SHOP", "SHOP", PostLogoutRedirectUris: [uri])).IsValid.Should().BeTrue();
        new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "SHOP", PostLogoutRedirectUris: [uri])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TheSignInListKeepsItsRule_ForNow()
    {
        // The printable-ASCII rule is the post-logout list's; the sign-in list's
        // own gap is a separate follow-up, so its rule is pinned unchanged here.
        new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("SHOP", "SHOP", RedirectUris: ["https://shop.example.com/\u00e9"])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_FiveHundredCharacters_IsTheLongestAccepted()
    {
        var uri = "https://shop.example.com/" + new string('a', 475);
        uri.Length.Should().Be(500);

        new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("SHOP", "SHOP", PostLogoutRedirectUris: [uri])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TwentyOneReturns_FailWithPostLogoutRedirectUrisTooMany()
    {
        var twenty = Enumerable.Range(1, 20).Select(i => $"https://shop.example.com/out/{i}").ToList();
        var twentyOne = twenty.Append("https://shop.example.com/out/21").ToList();

        new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("SHOP", "SHOP", PostLogoutRedirectUris: twenty)).IsValid.Should().BeTrue();

        var create = new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("SHOP", "SHOP", PostLogoutRedirectUris: twentyOne));
        var update = new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "SHOP", PostLogoutRedirectUris: twentyOne));

        Assert.Single(create.Errors).ErrorCode.Should().Be(ApplicationErrors.PostLogoutRedirectUrisTooMany.Code);
        Assert.Single(update.Errors).ErrorCode.Should().Be(ApplicationErrors.PostLogoutRedirectUrisTooMany.Code);
    }

    [Fact]
    public void Validate_AnInvalidReturn_DoesNotBorrowTheRedirectUriCode()
    {
        // A single failure carries no pointer (ADR 0001): the code alone must name
        // the list, or the console would point at the redirect URIs.
        var result = new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "SHOP",
                RedirectUris: ["https://shop.example.com/callback"],
                PostLogoutRedirectUris: ["http://shop.example.com/signed-out"]));

        result.Errors.Select(e => e.ErrorCode).Should().Equal(ApplicationErrors.PostLogoutRedirectUriInvalid.Code);
    }

    [Theory]
    [InlineData("Application.PostLogoutRedirectUriInvalid")]
    [InlineData("Application.PostLogoutRedirectUrisTooMany")]
    public void TheCodes_ArePublishedAs400WithThePointerToTheList(string code)
    {
        var published = PublishedErrorCodes.All[code];

        published.Status.Should().Be(400);
        published.Source.Should().Be("catalog");
        published.Pointer.Should().Be("#/postLogoutRedirectUris");
    }

    // --- the handlers: create, update and get carry it ---

    [Fact]
    public async Task Handle_CreateWithReturns_RegistersAndReturnsThem()
    {
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.ExistsByCodeAsync("SHOP", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ApplicationEntity? persisted = null;
        repository
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => persisted = app);

        var handler = new CreateApplicationCommandHandler(
            repository.Object,
            ApplicationTestImages.Composer(),
            ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
            new Mock<ILogger<CreateApplicationCommandHandler>>().Object);

        var result = await handler.Handle(
            new CreateApplicationCommand("SHOP", "SHOP", PostLogoutRedirectUris: [Return, $" {Return} ", LocalReturn])
            { CreatedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PostLogoutRedirectUris.Should().Equal(Return, LocalReturn);
        persisted!.PostLogoutRedirectUris.Should().Equal(Return, LocalReturn);
        persisted.RedirectUris.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CreateWithoutReturns_RegistersNone()
    {
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.ExistsByCodeAsync("SHOP", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        repository
            .Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);

        var handler = new CreateApplicationCommandHandler(
            repository.Object,
            ApplicationTestImages.Composer(),
            ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
            new Mock<ILogger<CreateApplicationCommandHandler>>().Object);

        var result = await handler.Handle(
            new CreateApplicationCommand("SHOP", "SHOP") { CreatedBy = Guid.NewGuid() }, CancellationToken.None);

        result.Value.PostLogoutRedirectUris.Should().BeEmpty();
    }

    private static async Task<(ErrorOr<ApplicationDto> Result, ApplicationEntity Saved)> UpdateWith(
        IReadOnlyList<string>? submitted)
    {
        var id = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: id, code: "SHOP", name: "SHOP");
        application.LoadRedirectUris(["https://shop.example.com/callback"]);
        application.LoadPostLogoutRedirectUris([Return]);

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
            new Mock<ILogger<UpdateApplicationCommandHandler>>().Object);

        var result = await handler.Handle(
            new UpdateApplicationCommand(id, "SHOP", PostLogoutRedirectUris: submitted) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        repository.Verify(r => r.UpdateAsync(application, It.IsAny<CancellationToken>()), Times.Once);
        return (result, application);
    }

    [Fact]
    public async Task Handle_UpdateWithNull_LeavesTheReturnsAsTheyAre()
    {
        var (result, saved) = await UpdateWith(null);

        result.Value.PostLogoutRedirectUris.Should().Equal(Return);
        saved.PostLogoutRedirectUris.Should().Equal(Return);
    }

    [Fact]
    public async Task Handle_UpdateWithEmpty_ClearsTheReturns()
    {
        var (result, saved) = await UpdateWith([]);

        result.Value.PostLogoutRedirectUris.Should().BeEmpty();
        saved.PostLogoutRedirectUris.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UpdateWithAList_ReplacesTheReturnsAndLeavesTheRedirectUris()
    {
        var (result, saved) = await UpdateWith([LocalReturn]);

        result.Value.PostLogoutRedirectUris.Should().Equal(LocalReturn);
        saved.PostLogoutRedirectUris.Should().Equal(LocalReturn);
        saved.RedirectUris.Should().Equal("https://shop.example.com/callback");
    }

    [Fact]
    public async Task Handle_GetById_ReturnsTheReturns()
    {
        var id = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(id: id, code: "SHOP");
        application.LoadPostLogoutRedirectUris([Return, LocalReturn]);
        var repository = new Mock<IApplicationRepository>();
        repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(application);

        var handler = new GetApplicationByIdQueryHandler(
            repository.Object,
            new Mock<IUserRepository>().Object,
            Mock.Of<IImageUrlComposer>(),
            new Mock<ILogger<GetApplicationByIdQueryHandler>>().Object);

        var result = await handler.Handle(new GetApplicationByIdQuery(id), CancellationToken.None);

        result.Value.PostLogoutRedirectUris.Should().Equal(Return, LocalReturn);
    }
}
