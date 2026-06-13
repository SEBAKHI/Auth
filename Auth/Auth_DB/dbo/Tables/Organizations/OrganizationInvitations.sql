CREATE TABLE [dbo].[OrganizationInvitations]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_OrganizationInvitations_Id] DEFAULT NEWID(),
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [Email] NVARCHAR(255) NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [Token] NVARCHAR(500) NOT NULL,
    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_OrganizationInvitations_Status] DEFAULT 'Pending',
    [ExpiresAt] DATETIME2 NOT NULL,
    [InvitedBy] UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt] DATETIME2 NULL,
    [AcceptedByUserId] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_OrganizationInvitations_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_OrganizationInvitations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OrganizationInvitations_Organizations] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrganizationInvitations_Roles] FOREIGN KEY ([RoleId])
        REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_OrganizationInvitations_InvitedBy] FOREIGN KEY ([InvitedBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_OrganizationInvitations_AcceptedBy] FOREIGN KEY ([AcceptedByUserId])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_OrganizationInvitations_Token] UNIQUE ([Token]),
    CONSTRAINT [CK_OrganizationInvitations_Status] CHECK ([Status] IN ('Pending', 'Accepted', 'Declined', 'Expired', 'Cancelled'))
);
GO

-- Status values:
-- Pending   = Invitation sent, awaiting response
-- Accepted  = User accepted the invitation
-- Declined  = User declined the invitation
-- Expired   = Invitation expired before response
-- Cancelled = Invitation was cancelled by the org admin

-- RoleId = the org-level role (org-owner, org-admin, org-member) to assign upon acceptance
-- Token = secure random token for accepting/declining via email link
-- AcceptedByUserId = the user who accepted (may differ from Email if user already has an account)

-- Indexes
CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_OrganizationId]
ON [dbo].[OrganizationInvitations] ([OrganizationId])
WHERE [Status] = 'Pending';
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_Email]
ON [dbo].[OrganizationInvitations] ([Email])
WHERE [Status] = 'Pending';
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_Token]
ON [dbo].[OrganizationInvitations] ([Token])
WHERE [Status] = 'Pending';
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_ExpiresAt]
ON [dbo].[OrganizationInvitations] ([ExpiresAt])
WHERE [Status] = 'Pending';
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_Status]
ON [dbo].[OrganizationInvitations] ([Status]);
GO
