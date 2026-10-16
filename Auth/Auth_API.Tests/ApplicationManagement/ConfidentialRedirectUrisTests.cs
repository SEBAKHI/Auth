using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.GetApplicationById;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ApplicationEntity = Auth.Domain.Entities.Application;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// OI-171: an application's server-side (confidential) redirect URIs, beside the public list. The
/// same rules and its own codes; an address is in one list or the other, held against the lists as
/// they stand after the save; the public list gains the printable-ASCII rule (OI-166 (a)); and a
/// change to any of the three lists is audited, before and after (OI-166 (c)).
/// </summary>
public class ConfidentialRedirectUrisTests
{
    private const string Public = "https://shop.example.com/callback";
    private const string ServerSide = "https://shop.example.com/bff/callback";
    private const string Return = "https://shop.example.com/signed-out";

    // --- the entity ---

    [Fact]
    public void IsConfidentialRedirectUriAllowed_MatchesExactly_AndKeepsTheListsApart()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetRedirectUris([Public], Guid.NewGuid());
        application.SetConfidentialRedirectUris([$" {ServerSide} ", ServerSide], Guid.NewGuid());

        application.ConfidentialRedirectUris.Should().Equal(ServerSide);
        application.IsConfidentialRedirectUriAllowed(ServerSide).Should().BeTrue();
        application.IsConfidentialRedirectUriAllowed(ServerSide.ToUpperInvariant()).Should().BeFalse();
        application.IsConfidentialRedirectUriAllowed(Public).Should().BeFalse();
        application.IsRedirectUriAllowed(ServerSide).Should().BeFalse();
        application.EnsureRedirectListsDisjoint().IsError.Should().BeFalse();
    }

    [Fact]
    public void EnsureRedirectListsDisjoint_AnAddressInBoth_IsRefused()
    {
        var application = TestHelpers.CreateApplication(code: "SHOP");
        application.SetRedirectUris([Public, ServerSide], Guid.NewGuid());
        application.SetConfidentialRedirectUris([ServerSide], Guid.NewGuid());

        application.EnsureRedirectListsDisjoint().FirstError.Code.Should().Be(ApplicationErrors.RedirectUriInBothLists.Code);
    }

    // --- the validators (TB9) ---

    public static TheoryData<string> Unprintable => new()
    {
        "https://shop.example.com/a\rb",
        "https://shop.example.com/a\nb",
        "https://shop.example.com/a\tb",
        "https://shop.example.com/a b",
        "https://shop.example.com/café",
    };

    [Theory]
    [MemberData(nameof(Unprintable))]
    public void Validate_BothSignInLists_RefuseControlSpaceAndNonAscii(string uri)
    {
        Codes(new CreateApplicationCommandValidator().Validate(new CreateApplicationCommand("SHOP", "SHOP", RedirectUris: [uri])))
            .Should().Equal(ApplicationErrors.RedirectUriInvalid.Code);
        Codes(new CreateApplicationCommandValidator().Validate(new CreateApplicationCommand("SHOP", "SHOP", ConfidentialRedirectUris: [uri])))
            .Should().Equal(ApplicationErrors.ConfidentialRedirectUriInvalid.Code);
        Codes(new UpdateApplicationCommandValidator().Validate(new UpdateApplicationCommand(Guid.NewGuid(), "SHOP", RedirectUris: [uri])))
            .Should().Equal(ApplicationErrors.RedirectUriInvalid.Code);
        Codes(new UpdateApplicationCommandValidator().Validate(new UpdateApplicationCommand(Guid.NewGuid(), "SHOP", ConfidentialRedirectUris: [uri])))
            .Should().Equal(ApplicationErrors.ConfidentialRedirectUriInvalid.Code);
    }

    [Theory]
    [InlineData("https://shop.example.com/caf%C3%A9?x=1")]
    [InlineData("http://localhost:5000/bff/callback")]
    public void Validate_PercentEncodingAndLocalhost_StayAllowed(string uri)
    {
        new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "SHOP", RedirectUris: [uri], ConfidentialRedirectUris: [uri + "/s"]))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://shop.example.com/bff")]
    [InlineData("https://shop.example.com/bff#fragment")]
    [InlineData("/relative")]
    public void Validate_TheServerSideList_FollowsThePublicListsRules(string uri)
    {
        Codes(new UpdateApplicationCommandValidator().Validate(new UpdateApplicationCommand(Guid.NewGuid(), "SHOP", ConfidentialRedirectUris: [uri])))
            .Should().Equal(ApplicationErrors.ConfidentialRedirectUriInvalid.Code);
    }

    [Fact]
    public void Validate_TwentyOneServerSideUris_AreTooMany()
    {
        var twentyOne = Enumerable.Range(1, 21).Select(i => $"https://shop.example.com/bff/{i}").ToList();

        Codes(new CreateApplicationCommandValidator().Validate(new CreateApplicationCommand("SHOP", "SHOP", ConfidentialRedirectUris: twentyOne)))
            .Should().Equal(ApplicationErrors.ConfidentialRedirectUrisTooMany.Code);
        new CreateApplicationCommandValidator().Validate(new CreateApplicationCommand("SHOP", "SHOP", ConfidentialRedirectUris: twentyOne.Take(20).ToList()))
            .IsValid.Should().BeTrue();
    }

    private static IEnumerable<string> Codes(FluentValidation.Results.ValidationResult result) =>
        result.Errors.Select(e => e.ErrorCode).Distinct();

    // --- the handlers ---

    private sealed class Harness
    {
        public Mock<IApplicationRepository> Repository { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<ApplicationRedirectUrisChangedEvent> Published { get; } = [];
        public ApplicationEntity Application { get; }

        public Harness()
        {
            Application = TestHelpers.CreateApplication(code: "SHOP", name: "SHOP");
            Application.LoadRedirectUris([Public]);
            Application.LoadPostLogoutRedirectUris([Return]);
            Repository.Setup(r => r.GetByIdAsync(Application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Application);
            Repository.Setup(r => r.ExistsByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Repository.Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);
            Publisher.Setup(p => p.Publish(It.IsAny<ApplicationRedirectUrisChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback((ApplicationRedirectUrisChangedEvent e, CancellationToken _) => Published.Add(e))
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<ApplicationDto>> Update(UpdateApplicationCommand command)
        {
            var permissions = new Mock<IPermissionRepository>();
            return new UpdateApplicationCommandHandler(
                    Repository.Object,
                    new Mock<ICredentialRevocationService>().Object,
                    ApplicationTestImages.Composer(),
                    new Auth.Application.Common.OrganizationCreatorRoleCheck(new Mock<IRoleRepository>().Object, permissions.Object),
                    new Auth.Application.Common.PermissionGrantGuard(permissions.Object),
                    ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
                    Publisher.Object,
                    new Mock<ILogger<UpdateApplicationCommandHandler>>().Object)
                .Handle(command, CancellationToken.None);
        }

        public Task<ErrorOr<ApplicationDto>> Create(CreateApplicationCommand command) =>
            new CreateApplicationCommandHandler(
                    Repository.Object,
                    ApplicationTestImages.Composer(),
                    ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
                    Publisher.Object,
                    new Mock<ILogger<CreateApplicationCommandHandler>>().Object)
                .Handle(command, CancellationToken.None);
    }

    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Update_TheServerSideList_IsSavedReturnedAndAudited()
    {
        var harness = new Harness();

        var result = await harness.Update(
            new UpdateApplicationCommand(harness.Application.Id, "SHOP", ConfidentialRedirectUris: [ServerSide]) { ModifiedBy = Admin });

        result.IsError.Should().BeFalse();
        result.Value.ConfidentialRedirectUris.Should().Equal(ServerSide);
        result.Value.RedirectUris.Should().Equal([Public], "null leaves the public list as it is");
        harness.Repository.Verify(r => r.UpdateAsync(harness.Application, It.IsAny<CancellationToken>()), Times.Once);

        var changed = harness.Published.Should().ContainSingle().Subject;
        changed.ApplicationId.Should().Be(harness.Application.Id);
        changed.TriggeredBy.Should().Be(Admin);
        changed.ConfidentialRedirectUris!.Before.Should().BeEmpty();
        changed.ConfidentialRedirectUris.After.Should().Equal(ServerSide);
        changed.RedirectUris.Should().BeNull("unchanged lists are not reported");
        changed.PostLogoutRedirectUris.Should().BeNull();
    }

    [Fact]
    public async Task Update_OnlyThePublicListSent_IsHeldAgainstTheStoredServerSideList()
    {
        var harness = new Harness();
        harness.Application.LoadConfidentialRedirectUris([ServerSide]);

        var result = await harness.Update(
            new UpdateApplicationCommand(harness.Application.Id, "SHOP", RedirectUris: [Public, ServerSide]) { ModifiedBy = Admin });

        result.FirstError.Code.Should().Be(ApplicationErrors.RedirectUriInBothLists.Code);
        harness.Repository.Verify(r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_MovingAnAddressBetweenTheLists_InOneSave_IsAllowed()
    {
        var harness = new Harness();

        var result = await harness.Update(new UpdateApplicationCommand(
            harness.Application.Id, "SHOP", RedirectUris: [], ConfidentialRedirectUris: [Public]) { ModifiedBy = Admin });

        result.IsError.Should().BeFalse();
        var changed = harness.Published.Should().ContainSingle().Subject;
        changed.RedirectUris!.Before.Should().Equal(Public);
        changed.RedirectUris.After.Should().BeEmpty();
        changed.ConfidentialRedirectUris!.After.Should().Equal(Public);
    }

    [Fact]
    public async Task Update_TheSameListsReordered_IsNoChange_AndAuditsNothing()
    {
        var harness = new Harness();
        harness.Application.LoadRedirectUris([Public, "https://shop.example.com/b"]);

        var result = await harness.Update(new UpdateApplicationCommand(
            harness.Application.Id, "SHOP", RedirectUris: ["https://shop.example.com/b", Public],
            PostLogoutRedirectUris: [Return]) { ModifiedBy = Admin });

        result.IsError.Should().BeFalse();
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_ThePostLogoutList_IsAuditedToo()
    {
        var harness = new Harness();

        await harness.Update(new UpdateApplicationCommand(
            harness.Application.Id, "SHOP", PostLogoutRedirectUris: []) { ModifiedBy = Admin });

        var changed = harness.Published.Should().ContainSingle().Subject;
        changed.PostLogoutRedirectUris!.Before.Should().Equal(Return);
        changed.PostLogoutRedirectUris.After.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithBothSignInLists_RegistersAndAuditsThem()
    {
        var harness = new Harness();

        var result = await harness.Create(new CreateApplicationCommand(
            "NEW", "New", RedirectUris: [Public], ConfidentialRedirectUris: [ServerSide]) { CreatedBy = Admin });

        result.IsError.Should().BeFalse();
        result.Value.ConfidentialRedirectUris.Should().Equal(ServerSide);
        var changed = harness.Published.Should().ContainSingle().Subject;
        changed.RedirectUris!.After.Should().Equal(Public);
        changed.ConfidentialRedirectUris!.After.Should().Equal(ServerSide);
        changed.PostLogoutRedirectUris.Should().BeNull();
    }

    [Fact]
    public async Task Create_AnAddressInBothLists_IsRefused_AndNothingIsStored()
    {
        var harness = new Harness();

        var result = await harness.Create(new CreateApplicationCommand(
            "NEW", "New", RedirectUris: [ServerSide], ConfidentialRedirectUris: [ServerSide]) { CreatedBy = Admin });

        result.FirstError.Code.Should().Be(ApplicationErrors.RedirectUriInBothLists.Code);
        harness.Repository.Verify(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WithNoLists_AuditsNothing()
    {
        var harness = new Harness();

        (await harness.Create(new CreateApplicationCommand("NEW", "New") { CreatedBy = Admin })).IsError.Should().BeFalse();

        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_ReturnsTheServerSideList()
    {
        var harness = new Harness();
        harness.Application.LoadConfidentialRedirectUris([ServerSide]);

        var result = await new GetApplicationByIdQueryHandler(
                harness.Repository.Object, new Mock<IUserRepository>().Object, ApplicationTestImages.Composer(),
                NullLogger<GetApplicationByIdQueryHandler>.Instance)
            .Handle(new GetApplicationByIdQuery(harness.Application.Id), CancellationToken.None);

        result.Value.ConfidentialRedirectUris.Should().Equal(ServerSide);
    }

    // --- the audit row (TB12) ---

    private static ApplicationRedirectUrisChangedEvent Changed() => new(
        Guid.NewGuid(), Guid.NewGuid(), "SHOP",
        new RedirectListChange([Public], []),
        new RedirectListChange([], [ServerSide]),
        null,
        Admin,
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task TheAuditRow_NamesEachChangedList_BeforeAndAfter()
    {
        var repository = new Mock<IAuditLogRepository>();
        AuditLogEntity? written = null;
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);
        var notification = Changed();

        await new ApplicationRedirectUrisChangedAuditEventHandler(
                repository.Object, NullLogger<ApplicationRedirectUrisChangedAuditEventHandler>.Instance)
            .Handle(notification, CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.ApplicationRedirectUrisChanged);
        written.ActionType.Should().Be(AuditActionTypes.Application);
        written.PerformedBy.Should().Be(Admin);
        written.EntityId.Should().Be(notification.ApplicationId);
        written.ApplicationId.Should().Be(notification.ApplicationId);
        using var data = JsonDocument.Parse(written.AdditionalData!);
        data.RootElement.GetProperty("redirectUris").GetProperty("Before")[0].GetString().Should().Be(Public);
        data.RootElement.GetProperty("redirectUris").GetProperty("After").GetArrayLength().Should().Be(0);
        data.RootElement.GetProperty("confidentialRedirectUris").GetProperty("After")[0].GetString().Should().Be(ServerSide);
        data.RootElement.GetProperty("postLogoutRedirectUris").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheAuditHandler_SwallowsAndLogsAFailure()
    {
        // The save has committed: a throwing handler would turn it into an error answer.
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var logger = new Mock<ILogger<ApplicationRedirectUrisChangedAuditEventHandler>>();

        var act = () => new ApplicationRedirectUrisChangedAuditEventHandler(repository.Object, logger.Object)
            .Handle(Changed(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<InvalidOperationException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
