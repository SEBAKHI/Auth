CREATE TABLE [dbo].[PermissionImplications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [ImpliedPermissionId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_PermissionImplications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_PermImpl_Permission] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [FK_PermImpl_Implied] FOREIGN KEY ([ImpliedPermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [UQ_PermissionImplication] UNIQUE ([PermissionId], [ImpliedPermissionId])
);
GO

-- Defines which permissions imply other permissions
-- Example: crm:leads:write implies crm:leads:read (you must see to edit)
-- Example: crm:leads:delete implies crm:leads:read (you must see to delete)
-- Example: crm:leads:* implies all crm:leads:* permissions

-- Indexes
CREATE NONCLUSTERED INDEX [IX_PermImpl_PermissionId]
ON [dbo].[PermissionImplications] ([PermissionId]);
GO

CREATE NONCLUSTERED INDEX [IX_PermImpl_ImpliedPermissionId]
ON [dbo].[PermissionImplications] ([ImpliedPermissionId]);
GO
