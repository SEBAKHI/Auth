CREATE TABLE [dbo].[OrganizationUserPermissions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [GrantedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_OrganizationUserPermissions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OrganizationUserPermissions_Organizations] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrganizationUserPermissions_Users] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_OrganizationUserPermissions_Applications] FOREIGN KEY ([ApplicationId])
        REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [FK_OrganizationUserPermissions_Permissions] FOREIGN KEY ([PermissionId])
        REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [FK_OrganizationUserPermissions_GrantedBy] FOREIGN KEY ([GrantedBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_OrganizationUserPermissions] UNIQUE ([OrganizationId], [UserId], [ApplicationId], [PermissionId])
);
GO

-- Represents individual permission grants within an organization
-- Example: User X has "data-transfer:export" permission in Organization Y for Application Z
-- This is for granular permission assignments that don't go through roles

-- Indexes
CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_OrganizationId]
ON [dbo].[OrganizationUserPermissions] ([OrganizationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_UserId]
ON [dbo].[OrganizationUserPermissions] ([UserId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_ApplicationId]
ON [dbo].[OrganizationUserPermissions] ([ApplicationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_PermissionId]
ON [dbo].[OrganizationUserPermissions] ([PermissionId]);
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_ExpiresAt]
ON [dbo].[OrganizationUserPermissions] ([ExpiresAt])
WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO

-- Composite index for permission checking
CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_Lookup]
ON [dbo].[OrganizationUserPermissions] ([UserId], [ApplicationId], [OrganizationId])
INCLUDE ([PermissionId])
WHERE [IsActive] = 1;
GO
