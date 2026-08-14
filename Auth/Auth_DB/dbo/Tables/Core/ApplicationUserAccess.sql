CREATE TABLE [dbo].[ApplicationUserAccess]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ApplicationUserAccess_Id] DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_ApplicationUserAccess_IsActive] DEFAULT 1,
    [GrantedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ApplicationUserAccess_GrantedAt] DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [Note] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ApplicationUserAccess_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_ApplicationUserAccess] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApplicationUserAccess_Applications] FOREIGN KEY ([ApplicationId])
        REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [FK_ApplicationUserAccess_Users] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_ApplicationUserAccess_GrantedBy] FOREIGN KEY ([GrantedBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_ApplicationUserAccess] UNIQUE ([ApplicationId], [UserId])
);
GO

-- The invitation list for an application in Restricted access mode: one row per
-- (application, user) meaning "this person may sign in to this application".
-- Read only while Applications.AccessMode = 2 (Restricted); an application open
-- to everyone never consults this table, so it stays as small as the number of
-- people an administrator invited by hand.
--
-- Revocation is soft: the row keeps IsActive = 0 with RevokedAt/RevokedBy so the
-- history of a past trial survives. Because of UQ_ApplicationUserAccess, granting
-- access to someone who was revoked earlier reactivates that same row rather than
-- inserting a second one.
--
-- No ON DELETE CASCADE on ApplicationId: applications are soft-deleted, so the
-- rows must outlive the delete (matches OrganizationApplications).

-- Indexes
CREATE NONCLUSTERED INDEX [IX_ApplicationUserAccess_ApplicationId]
ON [dbo].[ApplicationUserAccess] ([ApplicationId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_ApplicationUserAccess_UserId]
ON [dbo].[ApplicationUserAccess] ([UserId])
WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_ApplicationUserAccess_ExpiresAt]
ON [dbo].[ApplicationUserAccess] ([ExpiresAt])
WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO

-- Composite index for the sign-in gate: "is this user entitled to this app?"
CREATE NONCLUSTERED INDEX [IX_ApplicationUserAccess_Lookup]
ON [dbo].[ApplicationUserAccess] ([UserId], [ApplicationId])
INCLUDE ([ExpiresAt], [RevokedAt])
WHERE [IsActive] = 1;
GO
