CREATE TABLE [dbo].[OrganizationUserRoles]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OrganizationUserRoles_Id] DEFAULT NEWID(),
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_OrganizationUserRoles_IsActive] DEFAULT 1,
    [AssignedAt] DATETIME2 NOT NULL CONSTRAINT [DF_OrganizationUserRoles_AssignedAt] DEFAULT GETUTCDATE(),
    [AssignedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_OrganizationUserRoles_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_OrganizationUserRoles] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OrganizationUserRoles_Organizations] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrganizationUserRoles_Users] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_OrganizationUserRoles_Applications] FOREIGN KEY ([ApplicationId])
        REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [FK_OrganizationUserRoles_Roles] FOREIGN KEY ([RoleId])
        REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_OrganizationUserRoles_AssignedBy] FOREIGN KEY ([AssignedBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_OrganizationUserRoles] UNIQUE ([OrganizationId], [UserId], [ApplicationId], [RoleId])
);
GO

-- Represents app-level role assignments within an organization
-- Example: User X has "Data Transfer Editor" role in Organization Y for Application Z
-- RoleId references app-specific roles (not org-level roles like org-owner)
-- A user can have multiple roles for the same app in the same org

-- Indexes
CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_OrganizationId]
ON [dbo].[OrganizationUserRoles] ([OrganizationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_UserId]
ON [dbo].[OrganizationUserRoles] ([UserId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_ApplicationId]
ON [dbo].[OrganizationUserRoles] ([ApplicationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_RoleId]
ON [dbo].[OrganizationUserRoles] ([RoleId]);
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_ExpiresAt]
ON [dbo].[OrganizationUserRoles] ([ExpiresAt])
WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO

-- Composite index for permission checking
CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_Lookup]
ON [dbo].[OrganizationUserRoles] ([UserId], [ApplicationId], [OrganizationId])
INCLUDE ([RoleId])
WHERE [IsActive] = 1;
GO
