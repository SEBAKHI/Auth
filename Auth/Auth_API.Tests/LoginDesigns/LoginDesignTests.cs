using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Applications.GetPublicBranding;
using Auth.Application.Features.LoginDesigns.ActivateLoginDesign;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Application.Features.LoginDesigns.CopyLoginDesign;
using Auth.Application.Features.LoginDesigns.CreateLoginDesign;
using Auth.Application.Features.LoginDesigns.DeactivateLoginDesign;
using Auth.Application.Features.LoginDesigns.DeleteLoginDesign;
using Auth.Application.Features.LoginDesigns.GetLoginDesignById;
using Auth.Application.Features.LoginDesigns.GetPublicLoginDesign;
using Auth.Application.Features.LoginDesigns.PreviewLoginDesign;
using Auth.Application.Features.LoginDesigns.UpdateLoginDesign;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.LoginDesigns;
using Auth.Infrastructure.Notifications;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.LoginDesigns;

/// <summary>
/// Sign-in page designs (phase 6 of the accounts sign-in pages): free HTML, CSS and Liquid for
/// the panel half of the sign-in pages, per application and for the platform. Any number of
/// designs per scope, at most one active; a new design and a copy start inactive; saving the
/// active one is live; the active one cannot be deleted. Saving runs a gate: the Liquid parses,
/// every variable is one a page supplies (in every language), and the content fits in 64 KB.
/// </summary>
/// <remarks>
/// Deliberate breaks, each turning its tests red: make <c>LoginDesign.Create</c> start active;
/// drop the language loop from <c>LoginDesignRenderer.CheckForSave</c> (render English only);
/// remove <c>EnsureDeletable</c> from the delete handler; return success from the activate
/// handler when the repository reports a conflict; let <c>EffectiveLoginDesign</c> skip the
/// platform's fallback; remove the catch from one audit handler.
/// </remarks>
public class LoginDesignTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private const string PlatformName = "Platform Name";
    private const string AppName = "Application Name";

    private static LoginDesign Design(
        Guid? applicationId = null,
        string content = "<p>{{ App.Name }}</p>",
        string strings = "{}",
        bool showPlatformLogo = true) =>
        LoginDesign.Create(applicationId, "Design", content, strings, showPlatformLogo, Admin).Value;

    private static LoginDesign Active(Guid? applicationId = null, string content = "<p>{{ App.Name }}</p>")
    {
        var design = Design(applicationId, content);
        design.Activate();
        return design;
    }

    /// <summary>The handlers' collaborators, with a repository that answers from a list.</summary>
    private sealed class World
    {
        public Mock<ILoginDesignRepository> Designs { get; } = new();
        public Mock<IApplicationRepository> Applications { get; } = new();
        public Mock<IPlatformSettingsRepository> Platform { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<LoginDesign> Stored { get; } = [];
        public Application Application { get; }

        public World()
        {
            Application = TestHelpers.CreateApplication(id: AppId, code: "CRM", name: AppName);
            Applications.Setup(r => r.GetByIdAsync(AppId, It.IsAny<CancellationToken>())).ReturnsAsync(Application);
            Applications.Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>())).ReturnsAsync(Application);

            var settings = PlatformSettings.CreateDefault();
            settings.Update(PlatformName, null, null, null, Admin);
            Platform.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

            Designs.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Stored.SingleOrDefault(d => d.Id == id));
            Designs.Setup(r => r.GetActiveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid? scope, CancellationToken _) =>
                    Stored.SingleOrDefault(d => d.ApplicationId == scope && d.IsActive));
            Designs.Setup(r => r.CreateAsync(It.IsAny<LoginDesign>(), It.IsAny<CancellationToken>()))
                .Callback((LoginDesign design, CancellationToken _) => Stored.Add(design))
                .Returns(Task.CompletedTask);
            Designs.Setup(r => r.TryUpdateAsync(It.IsAny<LoginDesign>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            Designs.Setup(r => r.ActivateAsync(It.IsAny<LoginDesign>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LoginDesign design, CancellationToken _) =>
                {
                    var previous = Stored.SingleOrDefault(d => d.ApplicationId == design.ApplicationId && d.IsActive && d.Id != design.Id);
                    previous?.Deactivate();
                    return new LoginDesignActivation(true, previous?.Id);
                });
            Designs.Setup(r => r.DeactivateAsync(It.IsAny<LoginDesign>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Designs.Setup(r => r.DeleteInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        }

        public CountingTemplateRenderer Templates { get; } = new();
        public LoginDesignRenderCache Rendered { get; } = new();
        public LoginDesignRenderer Renderer() => new(Templates, Platform.Object, Applications.Object);
        public LoginDesignScope Scope() => new(Designs.Object, Applications.Object);
        public TimeProvider Time { get; } = new FixedTimeProvider(Now);

        public LoginDesign Store(LoginDesign design)
        {
            Stored.Add(design);
            return design;
        }

        public T Published<T>() where T : INotification =>
            (T)Publisher.Invocations.Single(i => i.Arguments[0] is T).Arguments[0];

        public void NothingPublished() => Publisher.Invocations.Should().BeEmpty();
    }

    /// <summary>The sandboxed renderer, counting the renders that reach it.</summary>
    private sealed class CountingTemplateRenderer : ITemplateRenderer
    {
        private readonly FluidTemplateRenderer _inner = new();

        public int Renders { get; private set; }

        public ErrorOr<string> Render(
            string source, IReadOnlyDictionary<string, object?> model, string languageCode, bool encodeHtml)
        {
            Renders++;
            return _inner.Render(source, model, languageCode, encodeHtml);
        }

        public ErrorOr<Success> Validate(string source) => _inner.Validate(source);

        public ErrorOr<string> RenderTracking(
            string source,
            IReadOnlyDictionary<string, object?> model,
            string languageCode,
            bool encodeHtml,
            out IReadOnlyList<string> unresolvedVariables)
        {
            Renders++;
            return _inner.RenderTracking(source, model, languageCode, encodeHtml, out unresolvedVariables);
        }
    }

    // --- the entity ---

    [Fact]
    public void Create_StartsInactive_WithTheFingerprintOfItsContentAndStrings()
    {
        var design = LoginDesign.Create(AppId, "  Spring  ", "<p>hi</p>", null, showPlatformLogo: false, Admin).Value;

        design.IsActive.Should().BeFalse("a new design reaches no visitor until it is activated");
        design.Name.Should().Be("Spring");
        design.StringsJson.Should().Be("{}");
        design.ShowPlatformLogo.Should().BeFalse();
        design.ContentHash.Should().Be(LoginDesign.Fingerprint("<p>hi</p>", "{}")).And.HaveLength(64);
    }

    [Fact]
    public void Fingerprint_FollowsTheContentAndTheStrings_NotTheName()
    {
        var design = Design(content: "<p>a</p>");
        var first = design.ContentHash;

        design.Update("Other name", "<p>a</p>", "{}", true, Admin);
        design.ContentHash.Should().Be(first);

        design.Update("Other name", "<p>b</p>", "{}", true, Admin);
        design.ContentHash.Should().NotBe(first);

        var withStrings = design.ContentHash;
        design.Update("Other name", "<p>b</p>", """{"en":{"x":"y"}}""", true, Admin);
        design.ContentHash.Should().NotBe(withStrings);
    }

    [Fact]
    public void Copy_IsANewInactiveDesignInTheSameScope_EvenOfTheActiveOne()
    {
        var original = Active(AppId, "<p>live</p>");

        var copy = original.Copy("Copy", Admin).Value;

        copy.Id.Should().NotBe(original.Id);
        copy.IsActive.Should().BeFalse();
        copy.ApplicationId.Should().Be(AppId);
        copy.Content.Should().Be("<p>live</p>");
        copy.ContentHash.Should().Be(original.ContentHash);
    }

    [Fact]
    public void Activate_And_Deactivate_ChangeOnlyTheState_AndAreIdempotent()
    {
        var design = Design();
        var revision = design.ModifiedAt ?? design.CreatedAt;

        design.Activate().Should().BeTrue();
        design.Activate().Should().BeFalse();
        design.IsActive.Should().BeTrue();
        design.Deactivate().Should().BeTrue();
        design.Deactivate().Should().BeFalse();
        design.IsRevision(revision).Should().BeTrue("switching a design on or off leaves an open editor's revision alone");
    }

    [Fact]
    public void TheActiveDesign_CannotBeDeleted()
    {
        Active().EnsureDeletable().FirstError.Should().Be(LoginDesignErrors.DeleteActive);
        Design().EnsureDeletable().IsError.Should().BeFalse();
    }

    [Fact]
    public void Content_Over64KB_IsRefused_AndExactly64KB_IsKept()
    {
        var limit = new string('a', LoginDesign.MaxContentBytes);
        LoginDesign.CheckContent(limit).IsError.Should().BeFalse();

        LoginDesign.CheckContent(limit + "a").FirstError.Should().Be(LoginDesignErrors.ContentTooLarge);
        // Bytes, not characters: 32,768 Arabic letters are 65,536 bytes of UTF-8.
        LoginDesign.CheckContent(new string('ع', 32_768) + "a").FirstError.Should().Be(LoginDesignErrors.ContentTooLarge);

        LoginDesign.Create(null, "Big", limit + "a", null, true, Admin).FirstError.Should().Be(LoginDesignErrors.ContentTooLarge);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"en":"text"}""")]
    [InlineData("""{"en":{"x":1}}""")]
    [InlineData("not json")]
    public void Strings_ThatAreNotLanguagesOfTexts_AreRefused(string strings)
    {
        LoginDesign.CheckStrings(strings).FirstError.Should().Be(LoginDesignErrors.StringsInvalidJson);
    }

    [Fact]
    public void Strings_Over64KB_AreRefused()
    {
        var big = JsonSerializer.Serialize(new { en = new { x = new string('a', LoginDesign.MaxStringsBytes) } });
        LoginDesign.CheckStrings(big).FirstError.Should().Be(LoginDesignErrors.StringsTooLarge);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", LoginDesignWarning.Script)]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", LoginDesignWarning.Script)]
    [InlineData("<img src=x onerror=\"alert(1)\">", LoginDesignWarning.EventHandler)]
    [InlineData("<div\nonclick='x()'>", LoginDesignWarning.EventHandler)]
    public void MarkupThatWillNotRun_IsReported(string content, LoginDesignWarning warning)
    {
        LoginDesign.WarningsOf(content).Should().Contain(warning);
    }

    [Fact]
    public void PlainMarkup_ReportsNothing_AndTextMentioningOn_IsNotAHandler()
    {
        LoginDesign.WarningsOf("<p class=\"one\">Turn on the lights; online = true</p>").Should().BeEmpty();
    }

    [Fact]
    public void TheWarningScan_IsLinear_AndStillFindsAHandlerAfterManyOpenTags()
    {
        // Every GET, save and preview of a design computes its warnings; a scan that rescans from
        // each "<" took 27 s on this 64 KB content.
        var openTags = string.Concat(Enumerable.Repeat("<a", 32_768));
        LoginDesign.WarningsOf("<a").Should().BeEmpty();

        var stopwatch = Stopwatch.StartNew();
        var warnings = LoginDesign.WarningsOf(openTags);
        stopwatch.Stop();

        warnings.Should().BeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(250));
        LoginDesign.WarningsOf(openTags + "<img src=x onerror=alert(1)>")
            .Should().Equal(LoginDesignWarning.EventHandler);
    }

    // --- the renderer and the save gate ---

    [Fact]
    public void Render_FillsTheModel_AndTheStringsOfTheLanguage_WithTheDefaultLanguageAsFallback()
    {
        var world = new World();
        var strings = """{"en":{"hi":"Hello {{ App.Name }}"},"ar":{"hi":"مرحبًا {{ App.Name }}"}}""";
        const string content = "<p lang=\"{{ lang }}\" dir=\"{{ dir }}\">{{ strings.hi | raw }} · {{ Platform.Name }}</p>";
        var audience = new LoginDesignAudience(AppName, PlatformName);

        world.Renderer().Render(content, strings, audience, "ar").Value
            .Should().Be($"<p lang=\"ar\" dir=\"rtl\">مرحبًا {AppName} · {PlatformName}</p>");
        world.Renderer().Render(content, strings, audience, "fr").Value
            .Should().Be($"<p lang=\"fr\" dir=\"ltr\">Hello {AppName} · {PlatformName}</p>",
                "a language without strings falls back to English");
    }

    [Fact]
    public void Render_EncodesEveryVariable()
    {
        var world = new World();
        var html = world.Renderer().Render(
            "<h1>{{ App.Name }}</h1>", "{}", new LoginDesignAudience("<b>&</b>", PlatformName), "en").Value;

        html.Should().Be("<h1>&lt;b&gt;&amp;&lt;/b&gt;</h1>");
    }

    [Fact]
    public void SaveGate_RefusesAVariableNoPageSupplies_EvenBehindALanguageCondition()
    {
        var world = new World();
        var audience = new LoginDesignAudience(AppName, PlatformName);

        var result = world.Renderer().CheckForSave(
            "{% if lang == 'fa' %}{{ Visitor.Email }}{% endif %}{{ App.Name }}", "{}", audience);

        result.FirstError.Code.Should().Be(LoginDesignErrors.UnknownVariables("").Code);
        result.FirstError.Description.Should().Contain("Visitor");
    }

    [Fact]
    public void SaveGate_RefusesAnUnknownVariableInAString()
    {
        var world = new World();

        var result = world.Renderer().CheckForSave(
            "<p>{{ strings.hi | raw }}</p>",
            """{"ar":{"hi":"{{ User.Name }}"}}""",
            new LoginDesignAudience(AppName, PlatformName));

        result.FirstError.Code.Should().Be("LoginDesign.UnknownVariables");
        result.FirstError.Description.Should().Contain("User");
    }

    [Fact]
    public void SaveGate_RefusesLiquidThatDoesNotParse()
    {
        var world = new World();

        var result = world.Renderer().CheckForSave(
            "{% if App.Name %}<p>open", "{}", new LoginDesignAudience(AppName, PlatformName));

        result.FirstError.Code.Should().Be("Notification.InvalidTemplateSyntax");
    }

    [Fact]
    public void Render_RefusesHtmlOver256KB_CountedInUtf8Bytes()
    {
        var world = new World();
        var audience = new LoginDesignAudience(AppName, PlatformName);
        const string loop = "{% for i in (1..16) %}{{ strings.big | raw }}{% endfor %}";
        var latin = JsonSerializer.Serialize(new { en = new { big = new string('a', 16_384) } });
        var arabic = JsonSerializer.Serialize(new { en = new { big = new string('ع', 8_192) } });

        world.Renderer().Render(loop, latin, audience, "en").Value.Length.Should().Be(LoginDesign.MaxRenderedBytes);
        world.Renderer().Render(loop + "b", latin, audience, "en")
            .FirstError.Should().Be(LoginDesignErrors.RenderedTooLarge);
        world.Renderer().Render(loop, arabic, audience, "en").IsError.Should().BeFalse();
        world.Renderer().Render(loop + "b", arabic, audience, "en").FirstError
            .Should().Be(LoginDesignErrors.RenderedTooLarge, "131,073 characters of Arabic are 262,145 bytes");
    }

    [Fact]
    public void SaveGate_AcceptsTheModel_AndTheStarterDesign()
    {
        var world = new World();
        var audience = new LoginDesignAudience(AppName, PlatformName);

        world.Renderer().CheckForSave(
            "{{ App.Name }} {{ Platform.Name }} {{ lang }} {{ dir }} {{ strings.x | raw }}", "{}", audience)
            .IsError.Should().BeFalse();
        world.Renderer().CheckForSave(LoginDesignStarter.Content, LoginDesignStarter.StringsJson, audience)
            .IsError.Should().BeFalse();
    }

    [Fact]
    public void TheStarter_HasItsStringsInEverySupportedLanguage_AndNamesNoColourOfItsOwn()
    {
        using var strings = JsonDocument.Parse(LoginDesignStarter.StringsJson);
        strings.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(Languages.Supported);
        LoginDesignStarter.Content.Should().NotMatchRegex(@"#[0-9a-fA-F]{3,8}\b|rgb\(|hsl\(|oklch\(",
            "everything it does not set follows the live appearance");
    }

    // --- create ---

    private static CreateLoginDesignCommandHandler CreateHandler(World world) => new(
        world.Designs.Object, world.Renderer(), world.Publisher.Object, world.Time,
        NullLogger<CreateLoginDesignCommandHandler>.Instance);

    [Fact]
    public async Task Create_StoresAnInactiveDesign_AndAnnouncesIt()
    {
        var world = new World();

        var result = await CreateHandler(world).Handle(
            new CreateLoginDesignCommand(AppId, "Autumn", "<p>{{ App.Name }}</p>", null, false) { CreatedBy = Admin },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsActive.Should().BeFalse();
        result.Value.ShowPlatformLogo.Should().BeFalse();
        world.Stored.Should().ContainSingle().Which.ApplicationId.Should().Be(AppId);

        var created = world.Published<LoginDesignCreatedEvent>();
        created.LoginDesignId.Should().Be(result.Value.Id);
        created.ApplicationId.Should().Be(AppId);
        created.CopiedFromId.Should().BeNull();
        created.TriggeredBy.Should().Be(Admin);
        created.OccurredAt.Should().Be(Now);
        created.Design.Should().Be(new LoginDesignSnapshot("Autumn", false, result.Value.ContentHash));
    }

    [Fact]
    public async Task Create_WithoutContent_StartsFromTheStarter()
    {
        var world = new World();

        var result = await CreateHandler(world).Handle(
            new CreateLoginDesignCommand(null, "Platform", null, null) { CreatedBy = Admin }, CancellationToken.None);

        result.Value.Content.Should().Be(LoginDesignStarter.Content);
        result.Value.StringsJson.Should().Be(LoginDesignStarter.StringsJson);
        result.Value.ApplicationId.Should().BeNull();
    }

    [Theory]
    [InlineData("{{ Visitor.Email }}", "LoginDesign.UnknownVariables")]
    [InlineData("{% for x in %}", "Notification.InvalidTemplateSyntax")]
    public async Task Create_RefusedByTheGate_StoresAndAnnouncesNothing(string content, string code)
    {
        var world = new World();

        var result = await CreateHandler(world).Handle(
            new CreateLoginDesignCommand(AppId, "Broken", content, null) { CreatedBy = Admin }, CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        world.Stored.Should().BeEmpty();
        world.NothingPublished();
    }

    [Fact]
    public async Task Create_OfALoopThatRendersFarMoreThanItsSource_IsRefused()
    {
        // 59 bytes of content and one 60 KB string render 144,000,000 characters: within the
        // renderer's step limit, and what every visitor would be served.
        var world = new World();
        const string content = "{% for i in (1..2400) %}{{ strings.big | raw }}{% endfor %}";
        var strings = JsonSerializer.Serialize(new { en = new { big = new string('a', 60_000) } });

        var result = await CreateHandler(world).Handle(
            new CreateLoginDesignCommand(AppId, "Loop", content, strings) { CreatedBy = Admin }, CancellationToken.None);

        result.FirstError.Should().Be(LoginDesignErrors.RenderedTooLarge);
        world.Stored.Should().BeEmpty();
        world.NothingPublished();
    }

    [Fact]
    public async Task Create_ForAMissingApplication_IsNotFound()
    {
        var world = new World();
        var other = Guid.NewGuid();

        var result = await CreateHandler(world).Handle(
            new CreateLoginDesignCommand(other, "x", "<p></p>", null) { CreatedBy = Admin }, CancellationToken.None);

        result.FirstError.Code.Should().Be(ApplicationErrors.NotFound(other).Code);
    }

    // --- update ---

    private static UpdateLoginDesignCommandHandler UpdateHandler(World world) => new(
        world.Designs.Object, world.Scope(), world.Renderer(), world.Publisher.Object, world.Time,
        NullLogger<UpdateLoginDesignCommandHandler>.Instance);

    [Fact]
    public async Task Update_OfTheActiveDesign_IsSavedLive_AndAnnouncedAsSuch()
    {
        var world = new World();
        var design = world.Store(Active(AppId, "<p>old</p>"));
        var before = design.ContentHash;

        var result = await UpdateHandler(world).Handle(
            new UpdateLoginDesignCommand(AppId, design.Id, "Renamed", "<p>new</p>", null, false, design.CreatedAt)
            { ModifiedBy = Admin },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.IsActive.Should().BeTrue("no draft: the active design stays active");
        world.Designs.Verify(r => r.TryUpdateAsync(design, design.CreatedAt, It.IsAny<CancellationToken>()), Times.Once);

        var updated = world.Published<LoginDesignUpdatedEvent>();
        updated.IsActive.Should().BeTrue();
        updated.Before.ContentHash.Should().Be(before);
        updated.After.Should().Be(new LoginDesignSnapshot("Renamed", false, result.Value.ContentHash));
    }

    [Fact]
    public async Task Update_AgainstAnOlderRevision_IsAConflict_AndWritesNothing()
    {
        var world = new World();
        var design = world.Store(Design(AppId));

        var result = await UpdateHandler(world).Handle(
            new UpdateLoginDesignCommand(AppId, design.Id, "x", "<p></p>", null, true, design.CreatedAt.AddSeconds(-1))
            { ModifiedBy = Admin },
            CancellationToken.None);

        result.FirstError.Should().Be(LoginDesignErrors.ConcurrencyConflict);
        world.Designs.Verify(r => r.TryUpdateAsync(It.IsAny<LoginDesign>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        world.NothingPublished();
    }

    [Fact]
    public async Task Update_LosingTheRaceInTheDatabase_IsAConflict_AndAnnouncesNothing()
    {
        var world = new World();
        var design = world.Store(Design(AppId));
        world.Designs.Setup(r => r.TryUpdateAsync(It.IsAny<LoginDesign>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await UpdateHandler(world).Handle(
            new UpdateLoginDesignCommand(AppId, design.Id, "x", "<p></p>", null, true, design.CreatedAt) { ModifiedBy = Admin },
            CancellationToken.None);

        result.FirstError.Should().Be(LoginDesignErrors.ConcurrencyConflict);
        world.NothingPublished();
    }

    [Fact]
    public async Task Update_RefusedByTheGate_WritesNothing()
    {
        var world = new World();
        var design = world.Store(Design(AppId));

        var result = await UpdateHandler(world).Handle(
            new UpdateLoginDesignCommand(AppId, design.Id, "x", "{{ Nope }}", null, true, design.CreatedAt) { ModifiedBy = Admin },
            CancellationToken.None);

        result.FirstError.Code.Should().Be("LoginDesign.UnknownVariables");
        world.Designs.Verify(r => r.TryUpdateAsync(It.IsAny<LoginDesign>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADesignOfAnotherScope_AnswersAsMissing()
    {
        var world = new World();
        var platformDesign = world.Store(Design(applicationId: null));

        var asApplication = await new GetLoginDesignByIdQueryHandler(world.Scope())
            .Handle(new GetLoginDesignByIdQuery(AppId, platformDesign.Id), CancellationToken.None);
        var otherApplicationsDesign = world.Store(Design(Guid.NewGuid()));
        var asPlatform = await new GetLoginDesignByIdQueryHandler(world.Scope())
            .Handle(new GetLoginDesignByIdQuery(null, otherApplicationsDesign.Id), CancellationToken.None);

        asApplication.FirstError.Code.Should().Be(LoginDesignErrors.NotFound(platformDesign.Id).Code);
        asPlatform.FirstError.Code.Should().Be(LoginDesignErrors.NotFound(otherApplicationsDesign.Id).Code);
    }

    // --- copy ---

    [Fact]
    public async Task Copy_OfTheActiveDesign_StartsInactive_AndNamesItsOriginal()
    {
        var world = new World();
        var original = world.Store(Active(AppId));

        var result = await new CopyLoginDesignCommandHandler(
                world.Designs.Object, world.Scope(), world.Publisher.Object, world.Time,
                NullLogger<CopyLoginDesignCommandHandler>.Instance)
            .Handle(new CopyLoginDesignCommand(AppId, original.Id, "Copy of it") { CreatedBy = Admin }, CancellationToken.None);

        result.Value.IsActive.Should().BeFalse();
        result.Value.Name.Should().Be("Copy of it");
        original.IsActive.Should().BeTrue("copying changes nothing visitors see");
        world.Published<LoginDesignCreatedEvent>().CopiedFromId.Should().Be(original.Id);
    }

    // --- activate / deactivate ---

    private static ActivateLoginDesignCommandHandler ActivateHandler(World world) => new(
        world.Designs.Object, world.Scope(), world.Publisher.Object, world.Time,
        NullLogger<ActivateLoginDesignCommandHandler>.Instance);

    [Fact]
    public async Task Activate_SwapsTheActiveDesign_AndNamesTheOneItReplaced()
    {
        var world = new World();
        var current = world.Store(Active(AppId));
        var next = world.Store(Design(AppId));

        var result = await ActivateHandler(world).Handle(
            new ActivateLoginDesignCommand(AppId, next.Id) { ActivatedBy = Admin }, CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
        current.IsActive.Should().BeFalse();
        world.Stored.Count(d => d.IsActive).Should().Be(1);
        var activated = world.Published<LoginDesignActivatedEvent>();
        activated.LoginDesignId.Should().Be(next.Id);
        activated.PreviousActiveId.Should().Be(current.Id);
    }

    [Fact]
    public async Task Activate_LosingTheRaceToAnotherActivation_Is409_AndAnnouncesNothing()
    {
        var world = new World();
        var design = world.Store(Design(AppId));
        world.Designs.Setup(r => r.ActivateAsync(It.IsAny<LoginDesign>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginDesignActivation.Conflict);

        var result = await ActivateHandler(world).Handle(
            new ActivateLoginDesignCommand(AppId, design.Id) { ActivatedBy = Admin }, CancellationToken.None);

        result.FirstError.Should().Be(LoginDesignErrors.ActivationConflict);
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        world.NothingPublished();
    }

    [Fact]
    public async Task Activate_OfTheActiveDesign_ChangesAndAnnouncesNothing()
    {
        var world = new World();
        var design = world.Store(Active(AppId));

        var result = await ActivateHandler(world).Handle(
            new ActivateLoginDesignCommand(AppId, design.Id) { ActivatedBy = Admin }, CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
        world.Designs.Verify(r => r.ActivateAsync(It.IsAny<LoginDesign>(), It.IsAny<CancellationToken>()), Times.Never);
        world.NothingPublished();
    }

    [Fact]
    public async Task Deactivate_SwitchesOff_AndAnnouncesIt_Once()
    {
        var world = new World();
        var design = world.Store(Active(null));
        var handler = new DeactivateLoginDesignCommandHandler(
            world.Designs.Object, world.Scope(), world.Publisher.Object, world.Time,
            NullLogger<DeactivateLoginDesignCommandHandler>.Instance);

        var first = await handler.Handle(new DeactivateLoginDesignCommand(null, design.Id) { DeactivatedBy = Admin }, CancellationToken.None);
        var second = await handler.Handle(new DeactivateLoginDesignCommand(null, design.Id) { DeactivatedBy = Admin }, CancellationToken.None);

        first.Value.IsActive.Should().BeFalse();
        second.Value.IsActive.Should().BeFalse();
        world.Publisher.Invocations.Count(i => i.Arguments[0] is LoginDesignDeactivatedEvent).Should().Be(1);
    }

    // --- delete ---

    private static DeleteLoginDesignCommandHandler DeleteHandler(World world) => new(
        world.Designs.Object, world.Scope(), world.Publisher.Object, world.Time,
        NullLogger<DeleteLoginDesignCommandHandler>.Instance);

    [Fact]
    public async Task Delete_OfTheActiveDesign_IsRefused_AndRemovesNothing()
    {
        var world = new World();
        var design = world.Store(Active(AppId));

        var result = await DeleteHandler(world).Handle(
            new DeleteLoginDesignCommand(AppId, design.Id) { DeletedBy = Admin }, CancellationToken.None);

        result.FirstError.Should().Be(LoginDesignErrors.DeleteActive);
        world.Designs.Verify(r => r.DeleteInactiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        world.NothingPublished();
    }

    [Fact]
    public async Task Delete_OfADesignActivatedSinceItWasRead_IsRefusedTheSameWay()
    {
        var world = new World();
        var design = world.Store(Design(AppId));
        world.Designs.Setup(r => r.DeleteInactiveAsync(design.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await DeleteHandler(world).Handle(
            new DeleteLoginDesignCommand(AppId, design.Id) { DeletedBy = Admin }, CancellationToken.None);

        result.FirstError.Should().Be(LoginDesignErrors.DeleteActive);
        world.NothingPublished();
    }

    [Fact]
    public async Task Delete_OfAnInactiveDesign_RemovesIt_AndAnnouncesIt()
    {
        var world = new World();
        var design = world.Store(Design(AppId));

        var result = await DeleteHandler(world).Handle(
            new DeleteLoginDesignCommand(AppId, design.Id) { DeletedBy = Admin }, CancellationToken.None);

        result.Value.Should().Be(Result.Deleted);
        world.Published<LoginDesignDeletedEvent>().LoginDesignId.Should().Be(design.Id);
    }

    // --- preview ---

    [Fact]
    public async Task Preview_RendersTheUnsavedDesign_WithItsWarnings_AndStoresNothing()
    {
        var world = new World();

        var result = await new PreviewLoginDesignQueryHandler(world.Renderer()).Handle(
            new PreviewLoginDesignQuery(AppId, "<p>{{ App.Name }}</p><script>x()</script>", null, "ur"),
            CancellationToken.None);

        result.Value.Html.Should().Be($"<p>{AppName}</p><script>x()</script>");
        result.Value.Dir.Should().Be("rtl");
        result.Value.Language.Should().Be("ur");
        result.Value.Warnings.Should().Equal(LoginDesignWarning.Script);
        world.Designs.Invocations.Should().BeEmpty();
    }

    // --- which design a page shows ---

    private static GetPublicLoginDesignQueryHandler PublicHandler(World world) => new(
        world.Applications.Object, new EffectiveLoginDesign(world.Designs.Object), world.Renderer(), world.Rendered);

    [Fact]
    public async Task APageRendersADesignOnce_PerVersionLanguageAndNames_AndServesItFromMemoryAfter()
    {
        var world = new World();
        var design = world.Store(Active(AppId, "<p>{{ App.Name }} · {{ Platform.Name }}</p>"));
        async Task<string> Page(string language) =>
            (await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("CRM", language), CancellationToken.None))
            .Value.Html;

        var first = await Page("ar");
        (await Page("ar")).Should().Be(first);
        world.Templates.Renders.Should().Be(1, "the second visitor is served the first render");

        await Page("fr");
        world.Templates.Renders.Should().Be(2, "each language renders once");

        design.Update("Design", "<p>{{ Platform.Name }}</p>", "{}", true, Admin);
        (await Page("ar")).Should().Be($"<p>{PlatformName}</p>", "a save changes the fingerprint");
        var renamed = PlatformSettings.CreateDefault();
        renamed.Update("Renamed", null, null, null, Admin);
        world.Platform.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(renamed);
        (await Page("ar")).Should().Be("<p>Renamed</p>", "a rename changes what the design reads");
        world.Templates.Renders.Should().Be(4);
    }

    [Fact]
    public async Task APageWhoseDesignRendersTooMuch_IsRefused_AndTheRefusalIsKept()
    {
        var world = new World();
        var big = JsonSerializer.Serialize(new { en = new { big = new string('a', 60_000) } });
        var design = Design(AppId, "{% for i in (1..5) %}{{ strings.big | raw }}{% endfor %}", big);
        design.Activate();
        world.Store(design);

        for (var visitor = 0; visitor < 3; visitor++)
        {
            (await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("CRM", "en"), CancellationToken.None))
                .FirstError.Should().Be(LoginDesignErrors.RenderedTooLarge);
        }

        world.Templates.Renders.Should().Be(2, "one string and the content, once");
    }

    [Fact]
    public async Task APageOfAnApplication_ShowsItsOwnActiveDesign_RenderedPerLanguage()
    {
        var world = new World();
        world.Store(Active(null, "<p>platform</p>"));
        var own = world.Store(Active(AppId, "<p dir=\"{{ dir }}\">{{ App.Name }}</p>"));

        var arabic = await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("CRM", "ar"), CancellationToken.None);
        var english = await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("CRM", "en-GB"), CancellationToken.None);

        arabic.Value.Id.Should().Be(own.Id);
        arabic.Value.ContentHash.Should().Be(own.ContentHash);
        arabic.Value.Html.Should().Be($"<p dir=\"rtl\">{AppName}</p>");
        arabic.Value.Dir.Should().Be("rtl");
        english.Value.Html.Should().Be($"<p dir=\"ltr\">{AppName}</p>");
        english.Value.Language.Should().Be("en");
    }

    [Fact]
    public async Task APageOfAnApplicationWithoutADesign_ShowsThePlatforms_WithTheApplicationsName()
    {
        var world = new World();
        var platform = world.Store(Active(null, "<p>{{ App.Name }} / {{ Platform.Name }}</p>"));
        world.Store(Design(AppId));

        var result = await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("CRM", "fr"), CancellationToken.None);

        result.Value.Id.Should().Be(platform.Id);
        result.Value.Html.Should().Be($"<p>{AppName} / {PlatformName}</p>");
    }

    [Fact]
    public async Task ThePlatformsOwnPages_ShowThePlatformsDesign_WithThePlatformsNameAsTheApplication()
    {
        var world = new World();
        world.Store(Active(AppId, "<p>application</p>"));
        world.Store(Active(null, "<p>{{ App.Name }}</p>"));

        var result = await PublicHandler(world).Handle(new GetPublicLoginDesignQuery(null, "xx"), CancellationToken.None);

        result.Value.Html.Should().Be($"<p>{PlatformName}</p>");
        result.Value.Language.Should().Be(Languages.Default, "an unsupported language renders the default one");
    }

    [Fact]
    public async Task NoActiveDesignAnywhere_IsNoneActive_AndAnUnknownOrInactiveApplicationIsNotFound()
    {
        var world = new World();

        (await PublicHandler(world).Handle(new GetPublicLoginDesignQuery(null, "en"), CancellationToken.None))
            .FirstError.Should().Be(LoginDesignErrors.NoneActive);
        (await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("CRM", "en"), CancellationToken.None))
            .FirstError.Should().Be(LoginDesignErrors.NoneActive);

        world.Store(Active(null));
        (await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("NOPE", "en"), CancellationToken.None))
            .FirstError.Code.Should().Be(ApplicationErrors.NotFoundByCode("NOPE").Code);
        world.Application.Deactivate(Admin);
        (await PublicHandler(world).Handle(new GetPublicLoginDesignQuery("CRM", "en"), CancellationToken.None))
            .FirstError.Code.Should().Be(ApplicationErrors.NotFoundByCode("CRM").Code);
    }

    [Fact]
    public async Task ThePublicBranding_NamesTheDesignThePageShows_WithItsVersionAndLogoChoice()
    {
        var world = new World();
        var platform = world.Store(Active(null));
        var handler = new GetPublicBrandingQueryHandler(
            world.Applications.Object, ApplicationTestImages.Composer(), new EffectiveLoginDesign(world.Designs.Object));

        var fallback = await handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);
        fallback.Value.LoginDesign.Should().Be(LoginDesignRefDto.FromOrNull(platform));

        var own = world.Store(Active(AppId));
        own.Update("Own", own.Content, own.StringsJson, showPlatformLogo: false, Admin);
        var mine = await handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None);
        mine.Value.LoginDesign.Should().Be(new LoginDesignRefDto
        {
            Id = own.Id,
            ContentHash = own.ContentHash,
            ShowPlatformLogo = false
        });

        world.Stored.Clear();
        (await handler.Handle(new GetPublicBrandingQuery("CRM"), CancellationToken.None))
            .Value.LoginDesign.Should().BeNull("no design anywhere: the plain panel");
    }

    // --- validators ---

    [Fact]
    public void Validators_UseTheDesignsOwnCodes()
    {
        var create = new CreateLoginDesignCommandValidator().Validate(
            new CreateLoginDesignCommand(null, "", new string('a', LoginDesign.MaxContentBytes + 1), "[]"));
        create.Errors.Select(e => e.ErrorCode).Should().BeEquivalentTo(
            LoginDesignErrors.NameRequired.Code, LoginDesignErrors.ContentTooLarge.Code, LoginDesignErrors.StringsInvalidJson.Code);

        var update = new UpdateLoginDesignCommandValidator().Validate(
            new UpdateLoginDesignCommand(null, Guid.NewGuid(), new string('n', 201), "", null, true, DateTime.UtcNow));
        update.Errors.Select(e => e.ErrorCode).Should().BeEquivalentTo(
            LoginDesignErrors.NameTooLong.Code, LoginDesignErrors.ContentRequired.Code);

        var preview = new PreviewLoginDesignQueryValidator().Validate(new PreviewLoginDesignQuery(null, "<p></p>", null, "de"));
        preview.Errors.Select(e => e.ErrorCode).Should().Equal(LoginDesignErrors.UnsupportedLanguage.Code);

        new CreateLoginDesignCommandValidator().Validate(new CreateLoginDesignCommand(null, "Starter", null, null))
            .IsValid.Should().BeTrue("no content starts from the starter");
    }

    // --- audit ---

    public static TheoryData<INotification> Events => new()
    {
        new LoginDesignCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), AppId, new("n", true, "h"), null, Admin, Now),
        new LoginDesignUpdatedEvent(Guid.NewGuid(), Guid.NewGuid(), AppId, true, new("a", true, "h1"), new("b", false, "h2"), Admin, Now),
        new LoginDesignActivatedEvent(Guid.NewGuid(), Guid.NewGuid(), null, new("n", true, "h"), Guid.NewGuid(), Admin, Now),
        new LoginDesignDeactivatedEvent(Guid.NewGuid(), Guid.NewGuid(), null, new("n", true, "h"), Admin, Now),
        new LoginDesignDeletedEvent(Guid.NewGuid(), Guid.NewGuid(), AppId, new("n", true, "h"), Admin, Now),
    };

    private static readonly Dictionary<Type, string> ActionOf = new()
    {
        [typeof(LoginDesignCreatedEvent)] = AuditActions.LoginDesignCreated,
        [typeof(LoginDesignUpdatedEvent)] = AuditActions.LoginDesignUpdated,
        [typeof(LoginDesignActivatedEvent)] = AuditActions.LoginDesignActivated,
        [typeof(LoginDesignDeactivatedEvent)] = AuditActions.LoginDesignDeactivated,
        [typeof(LoginDesignDeletedEvent)] = AuditActions.LoginDesignDeleted,
    };

    private static Task Audit(INotification notification, IAuditLogRepository repository) => notification switch
    {
        LoginDesignCreatedEvent e => new LoginDesignCreatedAuditEventHandler(repository, NullLogger<LoginDesignCreatedAuditEventHandler>.Instance).Handle(e, CancellationToken.None),
        LoginDesignUpdatedEvent e => new LoginDesignUpdatedAuditEventHandler(repository, NullLogger<LoginDesignUpdatedAuditEventHandler>.Instance).Handle(e, CancellationToken.None),
        LoginDesignActivatedEvent e => new LoginDesignActivatedAuditEventHandler(repository, NullLogger<LoginDesignActivatedAuditEventHandler>.Instance).Handle(e, CancellationToken.None),
        LoginDesignDeactivatedEvent e => new LoginDesignDeactivatedAuditEventHandler(repository, NullLogger<LoginDesignDeactivatedAuditEventHandler>.Instance).Handle(e, CancellationToken.None),
        LoginDesignDeletedEvent e => new LoginDesignDeletedAuditEventHandler(repository, NullLogger<LoginDesignDeletedAuditEventHandler>.Instance).Handle(e, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(notification)),
    };

    [Theory]
    [MemberData(nameof(Events))]
    public async Task EveryEvent_WritesItsAuditRow_UnderAdministration_WithoutTheContent(INotification notification)
    {
        AuditLogEntity? written = null;
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);

        await Audit(notification, repository.Object);

        written.Should().NotBeNull();
        written!.Action.Should().Be(ActionOf[notification.GetType()]);
        written.ActionType.Should().Be(AuditActionTypes.Administration);
        AuditActions.ByCode[written.Action].Should().Be(written.ActionType);
        written.EntityType.Should().Be("LoginDesign");
        written.PerformedBy.Should().Be(Admin);
    }

    [Theory]
    [MemberData(nameof(Events))]
    public async Task EveryAuditHandler_CatchesItsOwnFailure(INotification notification)
    {
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => Audit(notification, repository.Object);

        await act.Should().NotThrowAsync("the change has committed; a failed audit row must not fail it");
    }
}
