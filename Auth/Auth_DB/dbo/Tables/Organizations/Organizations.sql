CREATE TABLE [dbo].[Organizations]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Code] NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(255) NOT NULL,
    [Description] NVARCHAR(1000) NULL,
    [LogoUrl] NVARCHAR(500) NULL,
    [Website] NVARCHAR(500) NULL,
    [ContactEmail] NVARCHAR(255) NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_Organizations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Organizations_Users_Owner] FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_Organizations_Code] UNIQUE ([Code])
);
GO

-- Indexes
CREATE NONCLUSTERED INDEX [IX_Organizations_Code]
ON [dbo].[Organizations] ([Code])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Organizations_OwnerId]
ON [dbo].[Organizations] ([OwnerId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Organizations_IsActive]
ON [dbo].[Organizations] ([IsActive]);
GO
