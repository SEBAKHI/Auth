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
            SELECT [Id], [PlatformName], [LogoUrl], [LogoUrlDark], [FaviconUrl], [ModifiedAt], [ModifiedBy], [Theme], [LogoHeightPx]
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

    /// <inheritdoc />
    public async Task UpdateLogoHeightAsync(PlatformSettings settings, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(@"
            MERGE [dbo].[PlatformSettings] AS target
            USING (SELECT @Id AS [Id]) AS source
            ON target.[Id] = source.[Id]
            WHEN MATCHED THEN
                UPDATE SET
                    [LogoHeightPx] = @LogoHeightPx,
                    [ModifiedAt] = @ModifiedAt,
                    [ModifiedBy] = @ModifiedBy
            WHEN NOT MATCHED THEN
                INSERT ([Id], [PlatformName], [LogoHeightPx], [ModifiedAt], [ModifiedBy])
                VALUES (@Id, @PlatformName, @LogoHeightPx, @ModifiedAt, @ModifiedBy);",
            new
            {
                settings.Id,
                settings.PlatformName,
                LogoHeightPx = settings.LogoHeight?.Px,
                settings.ModifiedAt,
                settings.ModifiedBy
            },
            cancellationToken: cancellationToken));
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
    /// sizes and its own colours; so does a row whose sizes alone are invalid
    /// (an unknown step, a height out of bounds, or a value of the wrong JSON
    /// type), because a height cannot leave a palette half built. The sizes
    /// are therefore read field by field, never as part of the row's type.
    /// </remarks>
    public static class ThemeJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>A stored size that cannot be read; <see cref="PlatformTheme.Create"/> refuses it.</summary>
        private static readonly ThemeSizeChoice Unreadable = new(string.Empty);

        public static string? Write(PlatformTheme theme) =>
            theme == PlatformTheme.Default ? null : Serialize(theme);

        /// <summary>
        /// The stored form of any appearance, the shipped preset included: for an
        /// application, NULL means "none saved", not "the preset".
        /// </summary>
        public static string Serialize(PlatformTheme theme) =>
            JsonSerializer.Serialize(
                new ThemeRow(
                    Row(theme.Base),
                    Row(theme.Theme),
                    Row(theme.Chart),
                    theme.Radius,
                    theme.MenuAccent,
                    Element(theme.ControlSize),
                    Element(theme.TableRowHeight)),
                Options);

        public static PlatformTheme Read(string? json) => ReadOrNull(json) ?? PlatformTheme.Default;

        /// <summary>
        /// As <see cref="Read"/>, but null where nothing usable is stored: an
        /// application whose row holds no readable appearance shows the platform's.
        /// </summary>
        public static PlatformTheme? ReadOrNull(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var row = JsonSerializer.Deserialize<ThemeRow>(json, Options);
                if (row is null)
                {
                    return null;
                }

                var theme = Create(row, Size(row.ControlSize), Size(row.TableRowHeight));
                if (!theme.IsError)
                {
                    return theme.Value;
                }

                theme = Create(row, ThemeSizeChoice.Default, ThemeSizeChoice.Default);
                return theme.IsError ? null : theme.Value;
            }
            catch (JsonException)
            {
                return null;
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

        private static JsonElement Element(ThemeSizeChoice choice) =>
            JsonSerializer.SerializeToElement(new SizeRow(choice.Preset, choice.Px), Options);

        private static ThemeColorChoice? Choice(ChoiceRow? row) =>
            row is null ? null : new ThemeColorChoice(row.Preset, row.Light, row.Dark);

        /// <summary>
        /// A stored size: absent or null (saved before the size choices
        /// existed) reads as the default; anything but
        /// <c>{"preset": text, "px": whole number}</c> is unreadable and reads
        /// as a choice <see cref="PlatformTheme.Create"/> refuses, so only the
        /// sizes fall back.
        /// </summary>
        private static ThemeSizeChoice Size(JsonElement? stored)
        {
            if (stored is not { } element || element.ValueKind == JsonValueKind.Null)
            {
                return ThemeSizeChoice.Default;
            }

            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("preset", out var preset)
                || preset.ValueKind != JsonValueKind.String)
            {
                return Unreadable;
            }

            if (!element.TryGetProperty("px", out var px) || px.ValueKind == JsonValueKind.Null)
            {
                return new ThemeSizeChoice(preset.GetString()!);
            }

            return px.ValueKind == JsonValueKind.Number && px.TryGetInt32(out var value)
                ? new ThemeSizeChoice(preset.GetString()!, value)
                : Unreadable;
        }

        private sealed record ChoiceRow(string Preset, string? Light, string? Dark);

        private sealed record SizeRow(string Preset, int? Px);

        private sealed record ThemeRow(
            ChoiceRow? Base,
            ChoiceRow? Theme,
            ChoiceRow? Chart,
            string? Radius,
            string? MenuAccent,
            JsonElement? ControlSize,
            JsonElement? TableRowHeight);
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
        public int? LogoHeightPx { get; init; }

        public PlatformSettings ToEntity() => new(
            Id,
            PlatformName,
            LogoUrl,
            LogoUrlDark,
            FaviconUrl,
            ModifiedAt,
            ModifiedBy,
            ThemeJson.Read(Theme),
            LogoHeight.FromStored(LogoHeightPx));
    }
}
