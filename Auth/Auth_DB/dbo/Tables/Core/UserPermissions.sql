CREATE TABLE [dbo].[UserPermissions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_UserPermissions_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [GrantedAt] DATETIME2 NOT NULL CONSTRAINT [DF_UserPermissions_GrantedAt] DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_UserPermissions_IsActive] DEFAULT 1,

    CONSTRAINT [PK_UserPermissions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserPermissions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserPermissions_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [FK_UserPermissions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_UserPermissions] UNIQUE ([UserId], [PermissionId], [ApplicationId])
);
GO

-- Direct permission grants to users (bypassing roles)
-- ApplicationId NULL = global permission

-- Indexes
CREATE NONCLUSTERED INDEX [IX_UserPermissions_UserId]
ON [dbo].[UserPermissions] ([UserId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_UserPermissions_PermissionId]
ON [dbo].[UserPermissions] ([PermissionId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_UserPermissions_ApplicationId]
ON [dbo].[UserPermissions] ([ApplicationId])
WHERE [IsActive] = 1;
GO
