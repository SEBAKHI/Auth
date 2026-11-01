CREATE TABLE [dbo].[PlatformSettings]
(
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [PlatformName] NVARCHAR(255) NOT NULL CONSTRAINT [DF_PlatformSettings_PlatformName] DEFAULT N'Auth Console',
    [LogoUrl] NVARCHAR(500) NULL,
    [LogoUrlDark] NVARCHAR(500) NULL,
    [FaviconUrl] NVARCHAR(500) NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    -- Appearance chosen in the console (base colour, theme, chart colour,
    -- radius, menu accent) as JSON of registry names and #rrggbb colours.
    -- NULL = never customised: the shipped preset. Declared last so DacFx
    -- appends it instead of rebuilding the table.
    [Theme] NVARCHAR(1000) NULL,
    -- The height, in CSS pixels, the logo is shown at on the sign-in pages: the area it takes
    -- on the page, never the image. NULL = none chosen, the shipped default (LogoHeights in
    -- Auth.Domain). Declared last for the same reason as Theme.
    [LogoHeightPx] INT NULL,
    -- The platform's own fonts, one WOFF2 file per role (heading, body): the storage key of
    -- the file and the family name it declares, shown to administrators only. NULL key = the
    -- shipped IBM Plex Sans. Outside the Theme JSON, which is capped at 1000 characters.
    -- Declared last for the same reason as Theme.
    [HeadingFontKey] NVARCHAR(100) NULL,
    [HeadingFontName] NVARCHAR(100) NULL,
    [BodyFontKey] NVARCHAR(100) NULL,
    [BodyFontName] NVARCHAR(100) NULL,

    CONSTRAINT [PK_PlatformSettings] PRIMARY KEY CLUSTERED ([Id]),
    -- Single-row table: the only allowed row is the fixed singleton id.
    CONSTRAINT [CK_PlatformSettings_SingleRow] CHECK ([Id] = '30000000-0000-0000-0000-000000000001'),
    CONSTRAINT [CK_PlatformSettings_ThemeIsJson] CHECK ([Theme] IS NULL OR ISJSON([Theme]) = 1),
    -- The range of LogoHeights.MinPx and LogoHeights.MaxPx.
    CONSTRAINT [CK_PlatformSettings_LogoHeightPx] CHECK ([LogoHeightPx] IS NULL OR ([LogoHeightPx] >= 24 AND [LogoHeightPx] <= 120)),
    -- A font's name describes its file: no name without a file.
    CONSTRAINT [CK_PlatformSettings_HeadingFont] CHECK ([HeadingFontName] IS NULL OR [HeadingFontKey] IS NOT NULL),
    CONSTRAINT [CK_PlatformSettings_BodyFont] CHECK ([BodyFontName] IS NULL OR [BodyFontKey] IS NOT NULL)
);
GO
