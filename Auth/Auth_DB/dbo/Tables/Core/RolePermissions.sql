CREATE TABLE [dbo].[RolePermissions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_RolePermissions_Id] DEFAULT NEWID(),
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [GrantedAt] DATETIME2 NOT NULL CONSTRAINT [DF_RolePermissions_GrantedAt] DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_RolePermissions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_RolePermissions_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_RolePermissions_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [UQ_RolePermissions] UNIQUE ([RoleId], [PermissionId])
);
GO

-- Indexes
CREATE NONCLUSTERED INDEX [IX_RolePermissions_RoleId]
ON [dbo].[RolePermissions] ([RoleId]);
GO

CREATE NONCLUSTERED INDEX [IX_RolePermissions_PermissionId]
ON [dbo].[RolePermissions] ([PermissionId]);
GO
