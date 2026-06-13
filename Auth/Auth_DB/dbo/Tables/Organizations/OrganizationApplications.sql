CREATE TABLE [dbo].[OrganizationApplications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OrganizationApplications_Id] DEFAULT NEWID(),
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_OrganizationApplications_IsActive] DEFAULT 1,
    [EnabledAt] DATETIME2 NOT NULL CONSTRAINT [DF_OrganizationApplications_EnabledAt] DEFAULT GETUTCDATE(),
    [EnabledBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [SubscriptionTier] NVARCHAR(50) NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_OrganizationApplications_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_OrganizationApplications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OrganizationApplications_Organizations] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrganizationApplications_Applications] FOREIGN KEY ([ApplicationId])
        REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [FK_OrganizationApplications_EnabledBy] FOREIGN KEY ([EnabledBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_OrganizationApplications] UNIQUE ([OrganizationId], [ApplicationId])
);
GO

-- Represents an organization's subscription/enablement of an application
-- SubscriptionTier = optional tier like "free", "pro", "enterprise"
-- ExpiresAt = optional subscription expiry

-- Indexes
CREATE NONCLUSTERED INDEX [IX_OrganizationApplications_OrganizationId]
ON [dbo].[OrganizationApplications] ([OrganizationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationApplications_ApplicationId]
ON [dbo].[OrganizationApplications] ([ApplicationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationApplications_ExpiresAt]
ON [dbo].[OrganizationApplications] ([ExpiresAt])
WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO
