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

    CONSTRAINT [PK_PlatformSettings] PRIMARY KEY CLUSTERED ([Id]),
    -- Single-row table: the only allowed row is the fixed singleton id.
    CONSTRAINT [CK_PlatformSettings_SingleRow] CHECK ([Id] = '30000000-0000-0000-0000-000000000001'),
    CONSTRAINT [CK_PlatformSettings_ThemeIsJson] CHECK ([Theme] IS NULL OR ISJSON([Theme]) = 1)
);
GO
