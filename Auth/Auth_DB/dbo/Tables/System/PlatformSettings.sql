CREATE TABLE [dbo].[PlatformSettings]
(
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [PlatformName] NVARCHAR(255) NOT NULL CONSTRAINT [DF_PlatformSettings_PlatformName] DEFAULT N'Auth Console',
    [LogoUrl] NVARCHAR(500) NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_PlatformSettings] PRIMARY KEY CLUSTERED ([Id]),
    -- Single-row table: the only allowed row is the fixed singleton id.
    CONSTRAINT [CK_PlatformSettings_SingleRow] CHECK ([Id] = '30000000-0000-0000-0000-000000000001')
);
GO
