CREATE TABLE [dbo].[ExternalAuthProviders]
(
    [Id]            UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Code]          NVARCHAR(50)     NOT NULL,  -- 'google', 'apple', 'facebook'
    [Name]          NVARCHAR(100)    NOT NULL,  -- 'Google', 'Apple', 'Facebook'
    [IconUrl]       NVARCHAR(500)    NULL,       -- URL to provider icon/logo
    [IsEnabled]     BIT              NOT NULL DEFAULT 1,
    [DisplayOrder]  INT              NOT NULL DEFAULT 0,
    [CreatedAt]     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedAt]    DATETIME2        NULL,

    CONSTRAINT [PK_ExternalAuthProviders] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_ExternalAuthProviders_Code] UNIQUE ([Code])
);
GO

