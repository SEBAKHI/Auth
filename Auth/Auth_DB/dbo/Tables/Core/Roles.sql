CREATE TABLE [dbo].[Roles]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Roles_Id] DEFAULT NEWID(),
    [Code] NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [IsSystem] BIT NOT NULL CONSTRAINT [DF_Roles_IsSystem] DEFAULT 0,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Roles_IsActive] DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Roles_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_Roles] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Roles_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_Roles_Code_Application] UNIQUE ([Code], [ApplicationId])
);
GO

-- ApplicationId NULL = global role (applies to all applications)
-- IsSystem = 1 means the role cannot be deleted

-- Indexes
CREATE NONCLUSTERED INDEX [IX_Roles_ApplicationId]
ON [dbo].[Roles] ([ApplicationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Roles_Code]
ON [dbo].[Roles] ([Code])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Roles_IsSystem]
ON [dbo].[Roles] ([IsSystem])
WHERE [IsActive] = 1;
GO
