using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of the platform settings repository.
/// </summary>
public class PlatformSettingsRepository : IPlatformSettingsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PlatformSettingsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<PlatformSettings?> GetAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var dto = await connection.QueryFirstOrDefaultAsync<PlatformSettingsDto>(@"
            SELECT [Id], [PlatformName], [LogoUrl], [LogoUrlDark], [FaviconUrl], [ModifiedAt], [ModifiedBy], [Theme]
            FROM [dbo].[PlatformSettings]
            WHERE [Id] = @Id",
            new { Id = PlatformSettings.SingletonId });

        return dto?.ToEntity();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(PlatformSettings settings, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        // Upsert: the row is normally seeded at deploy time, but stay
        // resilient if the seed step has not run on this environment.
        await connection.ExecuteAsync(@"
            MERGE [dbo].[PlatformSettings] AS target
            USING (SELECT @Id AS [Id]) AS source
            ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [PlatformName] = @PlatformName,
                    [LogoUrl] = @LogoUrl,
                    [LogoUrlDark] = @LogoUrlDark,
                    [FaviconUrl] = @FaviconUrl,
                    [ModifiedAt] = @ModifiedAt,
                    [ModifiedBy] = @ModifiedBy
            WHEN NOT MATCHED THEN
                INSERT ([Id], [PlatformName], [LogoUrl], [LogoUrlDark], [FaviconUrl], [ModifiedAt], [ModifiedBy])
                VALUES (@Id, @PlatformName, @LogoUrl, @LogoUrlDark, @FaviconUrl, @ModifiedAt, @ModifiedBy);",
            new
            {
                settings.Id,
                settings.PlatformName,
                settings.LogoUrl,
                settings.LogoUrlDark,
                settings.FaviconUrl,
                settings.ModifiedAt,
                settings.ModifiedBy
            });
    }

    /// <inheritdoc />
    public async Task UpdateThemeAsync(PlatformSettings settings, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            MERGE [dbo].[PlatformSettings] AS target
            USING (SELECT @Id AS [Id]) AS source
            ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [Theme] = @Theme,
                    [ModifiedAt] = @ModifiedAt,
                    [ModifiedBy] = @ModifiedBy
            WHEN NOT MATCHED THEN
                INSERT ([Id], [PlatformName], [Theme], [ModifiedAt], [ModifiedBy])
                VALUES (@Id, @PlatformName, @Theme, @ModifiedAt, @ModifiedBy);",
            new
            {
                settings.Id,
                settings.PlatformName,
                Theme = ThemeJson.Write(settings.Theme),
                settings.ModifiedAt,
                settings.ModifiedBy
            });
    }

    /// <summary>
    /// The stored form of <see cref="PlatformTheme"/>: registry names,
    /// <c>#rrggbb</c> colours and size steps. Reading goes back through
    /// <see cref="PlatformTheme.Create"/>, so a row edited by hand into an
    /// invalid appearance (or naming a colour a later registry dropped) renders
    /// as the shipped preset rather than as a half-built palette.
    /// </summary>
    /// <remarks>
    /// Reading never fails: every sign-in page renders this value. A row saved
    /// before the size choices existed has none, and reads with the default
    /// sizes and its own colours; so does a row whose sizes alone are invalid,
    /// because a height cannot leave a palette half built.
    /// </remarks>
    public static class ThemeJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string? Write(PlatformTheme theme) =>
            theme == PlatformTheme.Default
                ? null
                : JsonSerializer.Serialize(
                    new ThemeRow(
                        Row(theme.Base),
                        Row(theme.Theme),
                        Row(theme.Chart),
                        theme.Radius,
                        theme.MenuAccent,
                        Row(theme.ControlSize),
                        Row(theme.TableRowHeight)),
                    Options);

        public static PlatformTheme Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return PlatformTheme.Default;
            }

            try
            {
                var row = JsonSerializer.Deserialize<ThemeRow>(json, Options);
                if (row is null)
                {
                    return PlatformTheme.Default;
                }

                var theme = Create(row, Size(row.ControlSize), Size(row.TableRowHeight));
                if (!theme.IsError)
                {
                    return theme.Value;
                }

                theme = Create(row, ThemeSizeChoice.Default, ThemeSizeChoice.Default);
                return theme.IsError ? PlatformTheme.Default : theme.Value;
            }
            catch (JsonException)
            {
                return PlatformTheme.Default;
            }
        }

        private static ErrorOr.ErrorOr<PlatformTheme> Create(
            ThemeRow row, ThemeSizeChoice controlSize, ThemeSizeChoice tableRowHeight) =>
            PlatformTheme.Create(
                Choice(row.Base),
                Choice(row.Theme),
                Choice(row.Chart),
                row.Radius,
                row.MenuAccent,
                controlSize,
                tableRowHeight);

        private static ChoiceRow Row(ThemeColorChoice choice) => new(choice.Preset, choice.Light, choice.Dark);

        private static SizeRow Row(ThemeSizeChoice choice) => new(choice.Preset, choice.Px);

        private static ThemeColorChoice? Choice(ChoiceRow? row) =>
            row is null ? null : new ThemeColorChoice(row.Preset, row.Light, row.Dark);

        // Absent: saved before the size choices existed.
        private static ThemeSizeChoice Size(SizeRow? row) =>
            row is null ? ThemeSizeChoice.Default : new ThemeSizeChoice(row.Preset, row.Px);

        private sealed record ChoiceRow(string Preset, string? Light, string? Dark);

        private sealed record SizeRow(string Preset, int? Px);

        private sealed record ThemeRow(
            ChoiceRow? Base,
            ChoiceRow? Theme,
            ChoiceRow? Chart,
            string? Radius,
            string? MenuAccent,
            SizeRow? ControlSize,
            SizeRow? TableRowHeight);
    }

    private record PlatformSettingsDto
    {
        public Guid Id { get; init; }
        public string PlatformName { get; init; } = string.Empty;
        public string? LogoUrl { get; init; }
        public string? LogoUrlDark { get; init; }
        public string? FaviconUrl { get; init; }
        public DateTime? ModifiedAt { get; init; }
        public Guid? ModifiedBy { get; init; }
        public string? Theme { get; init; }

        public PlatformSettings ToEntity() => new(
            Id,
            PlatformName,
            LogoUrl,
            LogoUrlDark,
            FaviconUrl,
            ModifiedAt,
            ModifiedBy,
            ThemeJson.Read(Theme));
    }
}
