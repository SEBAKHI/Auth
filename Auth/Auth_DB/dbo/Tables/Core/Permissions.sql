CREATE TABLE [dbo].[Permissions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Permissions_Id] DEFAULT NEWID(),
    [Code] NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [ParentId] UNIQUEIDENTIFIER NULL,
    [Level] TINYINT NOT NULL CONSTRAINT [DF_Permissions_Level] DEFAULT 0,
    [IsWildcard] BIT NOT NULL CONSTRAINT [DF_Permissions_IsWildcard] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Permissions_IsActive] DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Permissions_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_Permissions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Permissions_Parent] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [FK_Permissions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_Permissions_Code] UNIQUE ([Code])
);
GO

-- Code format: {application}:{resource}:{action}
-- Examples: crm:leads:read, crm:leads:write, crm:*, *
-- Level: 0=global(*), 1=application(crm:*), 2=resource(crm:leads:*), 3=action(crm:leads:read)
-- IsWildcard: true for permissions ending with :* or just *

-- Indexes
CREATE NONCLUSTERED INDEX [IX_Permissions_ParentId]
ON [dbo].[Permissions] ([ParentId]);
GO

CREATE NONCLUSTERED INDEX [IX_Permissions_ApplicationId]
ON [dbo].[Permissions] ([ApplicationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Permissions_Code]
ON [dbo].[Permissions] ([Code])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Permissions_Level]
ON [dbo].[Permissions] ([Level]);
GO

CREATE NONCLUSTERED INDEX [IX_Permissions_IsWildcard]
ON [dbo].[Permissions] ([IsWildcard])
WHERE [IsActive] = 1;
GO
