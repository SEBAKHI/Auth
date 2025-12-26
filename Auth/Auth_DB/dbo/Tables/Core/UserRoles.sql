CREATE TABLE [dbo].[UserRoles]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [AssignedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [AssignedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,

    CONSTRAINT [PK_UserRoles] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserRoles_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserRoles_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_UserRoles_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_UserRoles] UNIQUE ([UserId], [RoleId], [ApplicationId])
);
GO

-- ApplicationId NULL = global assignment (role applies to all applications)
-- ExpiresAt = optional time-limited role assignment

-- Indexes
CREATE NONCLUSTERED INDEX [IX_UserRoles_UserId]
ON [dbo].[UserRoles] ([UserId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_UserRoles_RoleId]
ON [dbo].[UserRoles] ([RoleId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_UserRoles_ApplicationId]
ON [dbo].[UserRoles] ([ApplicationId]);
GO

CREATE NONCLUSTERED INDEX [IX_UserRoles_ExpiresAt]
ON [dbo].[UserRoles] ([ExpiresAt])
WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO
