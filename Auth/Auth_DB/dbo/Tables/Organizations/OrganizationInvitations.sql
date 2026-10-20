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
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [ApplicationRoleId] UNIQUEIDENTIFIER NULL,
    [ApplicationRoleOutcome] NVARCHAR(16) NULL,

    CONSTRAINT [PK_OrganizationInvitations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OrganizationInvitations_Organizations] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organizations]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrganizationInvitations_Roles] FOREIGN KEY ([RoleId])
        REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_OrganizationInvitations_InvitedBy] FOREIGN KEY ([InvitedBy])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_OrganizationInvitations_AcceptedBy] FOREIGN KEY ([AcceptedByUserId])
        REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_OrganizationInvitations_Applications] FOREIGN KEY ([ApplicationId])
        REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [FK_OrganizationInvitations_ApplicationRoles] FOREIGN KEY ([ApplicationRoleId])
        REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [UQ_OrganizationInvitations_Token] UNIQUE ([Token]),
    CONSTRAINT [CK_OrganizationInvitations_ApplicationRole] CHECK (
        ([ApplicationId] IS NULL AND [ApplicationRoleId] IS NULL)
        OR ([ApplicationId] IS NOT NULL AND [ApplicationRoleId] IS NOT NULL)),
    CONSTRAINT [CK_OrganizationInvitations_Status] CHECK ([Status] IN ('Pending', 'Accepted', 'Declined', 'Expired', 'Cancelled')),
    CONSTRAINT [CK_OrganizationInvitations_ApplicationRoleOutcome] CHECK (
        [ApplicationRoleOutcome] IS NULL OR [ApplicationRoleOutcome] IN ('Applied', 'AlreadyHeld', 'Dropped')),
    CONSTRAINT [CK_OrganizationInvitations_ApplicationRoleOutcomeNeedsRole] CHECK (
        [ApplicationRoleOutcome] IS NULL OR [ApplicationId] IS NOT NULL)
);
GO

-- Status values:
-- Pending   = Invitation sent, awaiting response
-- Accepted  = User accepted the invitation
-- Declined  = User declined the invitation
-- Expired   = Invitation expired before response
-- Cancelled = Invitation was cancelled by the org admin

-- RoleId = the org-level role (org-owner, org-admin, org-member) to assign upon acceptance
--
-- Token = the HMAC-SHA256 HASH of the invitation token, base64, 44 characters.
--         NOT the token. The plaintext lives in the invitation e-mail and in the
--         request that redeems it, and is never stored. The column keeps its old
--         name because renaming it would be a DACPAC drop-and-add on a
--         UNIQUE-constrained column; the domain entity is called TokenHash.
--         The UNIQUE constraint still holds: a hash is as unique as its token.
-- AcceptedByUserId = the user who accepted (may differ from Email if user already has an account)
--
-- ApplicationId + ApplicationRoleId = an application role assigned on acceptance alongside
--         the membership role (an OrganizationUserRoles row), or both NULL for membership
--         only. Both or neither, by CK_OrganizationInvitations_ApplicationRole. The columns
--         come last so a DACPAC publish adds them to the existing table instead of
--         rebuilding it.
-- ApplicationRoleOutcome = what acceptance did with that application role, written in the
--         acceptance's own transaction: Applied (a new assignment), AlreadyHeld (the invitee
--         had it), Dropped (the inviter could no longer give it, so the invitee joined
--         without it). NULL while pending, and always NULL on a membership-only invitation
--         (CK_OrganizationInvitations_ApplicationRoleOutcomeNeedsRole). Last, like the two above.

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

-- An organization's invitation list by status (OI-193's list, OI-194's application directory):
-- every status, not only Pending, so it is not the filtered index above. Seeks on the
-- organization and the stored status; the INCLUDEs answer the effective-status test, the
-- newest-first order and the application filter without a key lookup per row.
CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_OrganizationId_Status]
ON [dbo].[OrganizationInvitations] ([OrganizationId], [Status])
INCLUDE ([ExpiresAt], [CreatedAt], [ApplicationId]);
GO
