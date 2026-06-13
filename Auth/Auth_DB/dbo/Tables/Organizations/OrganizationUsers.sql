CREATE TABLE [dbo].[OrganizationUsers]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OrganizationUsers_Id] DEFAULT NEWID(),
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_OrganizationUsers_IsActive] DEFAULT 1,
    [JoinedAt] DATETIME2 NOT NULL CONSTRAINT [DF_OrganizationUsers_JoinedAt] DEFAULT GETUTCDATE(),
    [InvitedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_OrganizationUsers_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_OrganizationUsers] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OrganizationUsers_Organizations] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrganizationUsers_Users] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_OrganizationUsers_Roles] FOREIGN KEY ([RoleId])
        REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_OrganizationUsers_InvitedBy] FOREIGN KEY ([InvitedBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_OrganizationUsers] UNIQUE ([OrganizationId], [UserId])
);
GO

-- RoleId references organization-level roles: org-owner, org-admin, org-member
-- InvitedBy tracks who added this user to the organization
-- ExpiresAt = optional time-limited membership

-- Indexes
CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_OrganizationId]
ON [dbo].[OrganizationUsers] ([OrganizationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_UserId]
ON [dbo].[OrganizationUsers] ([UserId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_RoleId]
ON [dbo].[OrganizationUsers] ([RoleId]);
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_ExpiresAt]
ON [dbo].[OrganizationUsers] ([ExpiresAt])
WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO
