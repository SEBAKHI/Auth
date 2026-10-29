using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// An application's own appearance survives the database round trip: every read that builds an
/// application selects and hydrates it, the dedicated write stores the switch and the theme and
/// nothing else, and the general update never writes them (a logo upload built from an older
/// copy cannot put back an appearance switched off since).
/// </summary>
/// <remarks>
/// Deliberate breaks: drop <c>[Theme], [ThemeEnabled]</c> from one SELECT (its read case turns
/// red); write <c>ThemeJson.Write</c> instead of <c>Serialize</c> (the shipped-preset case turns red).
/// </remarks>
public class ApplicationAppearancePersistenceTests
{
    private static PlatformTheme Theme(string radius = "large") => PlatformTheme.Create(
        new ThemeColorChoice("custom", "#112233", "#445566"),
        new ThemeColorChoice("violet"),
        new ThemeColorChoice("cyan"),
        radius,
        "bold",
        new ThemeSizeChoice("compact"),
        new ThemeSizeChoice("custom", 52)).Value;

    /// <summary>A database whose every application row has an appearance switched on.</summary>
    private static RecordingDbConnectionFactory SwitchedOnDatabase() =>
        new(
            affectedRows: 0,
            rowFor: command => command.CommandText.Contains("FROM [dbo].[Applications]", StringComparison.Ordinal)
                && !command.CommandText.Contains("COUNT(", StringComparison.Ordinal)
                ? new
                {
                    Id = Guid.NewGuid(),
                    Code = "CRM",
                    Name = "CRM",
                    IsActive = true,
                    SessionTimeoutMinutes = 60,
                    MaxConcurrentSessions = 5,
                    AccessMode = (byte)1,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = Guid.NewGuid(),
                    AllowMachineOrganizationsRead = true,
                    Theme = PlatformSettingsRepository.ThemeJson.Serialize(Theme()),
                    ThemeEnabled = true
                }
                : null);

    public static TheoryData<string> Reads => new()
    {
        nameof(ApplicationRepository.GetByIdAsync),
        nameof(ApplicationRepository.GetByIdIncludingDeletedAsync),
        nameof(ApplicationRepository.GetByCodeAsync),
        nameof(ApplicationRepository.GetAllAsync),
        nameof(ApplicationRepository.GetActiveAsync),
        nameof(ApplicationRepository.GetPagedAsync),
    };

    [Fact]
    public void Reads_IsNotEmpty() => Reads.Count<object[]>().Should().Be(6);

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task EveryApplicationRead_SelectsTheAppearance_AndHydratesIt(string read)
    {
        var db = SwitchedOnDatabase();
        var repository = new ApplicationRepository(db);

        IReadOnlyList<AppEntity?> applications = read switch
        {
            nameof(ApplicationRepository.GetByIdAsync) => new AppEntity?[] { await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None) },
            nameof(ApplicationRepository.GetByIdIncludingDeletedAsync) => new AppEntity?[] { await repository.GetByIdIncludingDeletedAsync(Guid.NewGuid(), CancellationToken.None) },
            nameof(ApplicationRepository.GetByCodeAsync) => new AppEntity?[] { await repository.GetByCodeAsync("CRM", CancellationToken.None) },
            nameof(ApplicationRepository.GetAllAsync) => (IReadOnlyList<AppEntity?>)await repository.GetAllAsync(CancellationToken.None),
            nameof(ApplicationRepository.GetActiveAsync) => (IReadOnlyList<AppEntity?>)await repository.GetActiveAsync(CancellationToken.None),
            _ => (IReadOnlyList<AppEntity?>)(await repository.GetPagedAsync(1, 20, null, null, null, SortDirection.Asc, CancellationToken.None)).Applications,
        };

        var select = db.Commands.First(command =>
            command.CommandText.Contains("FROM [dbo].[Applications]", StringComparison.Ordinal)
            && !command.CommandText.Contains("COUNT(", StringComparison.Ordinal));
        select.CommandText.Should().Contain("[Theme]").And.Contain("[ThemeEnabled]",
            "{0} must read the appearance, or a visitor sees the platform's", read);
        applications.Should().NotBeEmpty().And.OnlyContain(application => application != null);
        applications.Should().OnlyContain(application => application!.ThemeEnabled);
        applications.Should().OnlyContain(application => application!.EffectiveTheme == Theme());
    }

    [Fact]
    public async Task UpdateAppearanceAsync_WritesTheSwitchAndTheTheme_AndNothingElse()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var application = TestHelpers.CreateApplication();
        application.SetAppearance(true, Theme(), Guid.NewGuid());

        var written = await new ApplicationRepository(db).UpdateAppearanceAsync(application, CancellationToken.None);

        written.Should().BeTrue();
        var update = db.Commands.Should().ContainSingle().Subject;
        update.CommandText.Should().Contain("UPDATE [dbo].[Applications] SET")
            .And.Contain("[Theme] = @Theme")
            .And.Contain("[ThemeEnabled] = @ThemeEnabled")
            .And.Contain("[IsDeleted] = 0")
            .And.NotContain("[Name]")
            .And.NotContain("[LogoUrl]");
        update.Parameters["ThemeEnabled"].Should().Be(true);
        PlatformSettingsRepository.ThemeJson.ReadOrNull((string)update.Parameters["Theme"]!).Should().Be(Theme());
    }

    [Fact]
    public async Task UpdateAppearanceAsync_StoresTheShippedPresetInFull()
    {
        // An application may choose the shipped preset while the platform shows another: its
        // NULL would read back as "none saved", and its visitors would get the platform's.
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var application = TestHelpers.CreateApplication();
        application.SetAppearance(true, PlatformTheme.Default, Guid.NewGuid());

        await new ApplicationRepository(db).UpdateAppearanceAsync(application, CancellationToken.None);

        var stored = db.Commands.Should().ContainSingle().Subject.Parameters["Theme"] as string;
        PlatformSettingsRepository.ThemeJson.ReadOrNull(stored).Should().Be(PlatformTheme.Default);
    }

    [Fact]
    public async Task UpdateAppearanceAsync_ReportsAGoneApplication()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var application = TestHelpers.CreateApplication();
        application.SetAppearance(true, Theme(), Guid.NewGuid());

        (await new ApplicationRepository(db).UpdateAppearanceAsync(application, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_NeverWritesTheAppearance()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var application = TestHelpers.CreateApplication();
        application.SetAppearance(true, Theme(), Guid.NewGuid());

        await new ApplicationRepository(db).UpdateAsync(application, CancellationToken.None);

        var update = db.Commands.First(command => command.CommandText.Contains("UPDATE [dbo].[Applications]", StringComparison.Ordinal));
        update.CommandText.Should().NotContain("[Theme]").And.NotContain("[ThemeEnabled]");
    }

    [Fact]
    public async Task CreateAsync_WritesTheAppearanceItWasGiven()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var application = AppEntity.Create("CRM", "Example CRM", null, null, Guid.NewGuid());

        await new ApplicationRepository(db).CreateAsync(application, CancellationToken.None);

        var insert = db.Commands.First(command => command.CommandText.Contains("INSERT INTO [dbo].[Applications]", StringComparison.Ordinal));
        insert.CommandText.Should().Contain("@Theme").And.Contain("@ThemeEnabled");
        insert.Parameters["Theme"].Should().BeNull("a new application has saved no appearance");
        insert.Parameters["ThemeEnabled"].Should().Be(false);
    }

    // --- the stored form ---

    [Fact]
    public void TheShippedPreset_IsStoredInFull_ForAnApplication()
    {
        // NULL means "none saved" for an application: an application whose own appearance IS
        // the shipped preset must not read back as "show the platform's".
        var json = PlatformSettingsRepository.ThemeJson.Serialize(PlatformTheme.Default);

        json.Should().NotBeNullOrWhiteSpace();
        PlatformSettingsRepository.ThemeJson.ReadOrNull(json).Should().Be(PlatformTheme.Default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"base\":{\"preset\":\"not-a-colour\"}}")]
    public void AnUnreadableRow_ReadsAsNone_ForAnApplication_AndAsThePreset_ForThePlatform(string? stored)
    {
        PlatformSettingsRepository.ThemeJson.ReadOrNull(stored).Should().BeNull();
        PlatformSettingsRepository.ThemeJson.Read(stored).Should().Be(PlatformTheme.Default);
    }

    [Fact]
    public void ARowWhoseSizesAloneAreInvalid_KeepsItsColours_AsForThePlatform()
    {
        var json = PlatformSettingsRepository.ThemeJson.Serialize(Theme())
            .Replace("\"px\":52", "\"px\":500", StringComparison.Ordinal);

        var read = PlatformSettingsRepository.ThemeJson.ReadOrNull(json);

        read.Should().NotBeNull();
        read!.Base.Should().Be(new ThemeColorChoice("custom", "#112233", "#445566"));
        read.TableRowHeight.Should().Be(ThemeSizeChoice.Default);
    }
}
