CREATE TABLE [dbo].[AccountDeletionRequests]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AccountDeletionRequests_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Status] TINYINT NOT NULL CONSTRAINT [DF_AccountDeletionRequests_Status] DEFAULT 1,
    [Source] TINYINT NOT NULL,
    [RequestedAtUtc] DATETIME2 NOT NULL CONSTRAINT [DF_AccountDeletionRequests_RequestedAtUtc] DEFAULT GETUTCDATE(),
    [GraceEndsAtUtc] DATETIME2 NOT NULL,
    [CancelledAtUtc] DATETIME2 NULL,
    [CompletedAtUtc] DATETIME2 NULL,
    [PolicyVersion] NVARCHAR(20) NOT NULL,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_AccountDeletionRequests_AttemptCount] DEFAULT 0,
    [LastError] NVARCHAR(2000) NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_AccountDeletionRequests_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,

    CONSTRAINT [PK_AccountDeletionRequests] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_AccountDeletionRequests_Status] CHECK ([Status] IN (1, 2, 3, 4, 5)),
    CONSTRAINT [CK_AccountDeletionRequests_Source] CHECK ([Source] IN (1, 2))
);
GO

-- Status values: 1=PendingGrace, 2=Cancelled, 3=Processing, 4=Completed, 5=Failed
-- Source values: 1=InApp, 2=PublicWeb
-- UserId is a loose reference (no FK): terminal rows are destruction evidence
-- retained >= 3 years and must survive the Users row purge (precedent:
-- NotificationOutbox.RecipientUserId).
-- LastError must never contain PII (it records e.g. an Apple revocation failure).

-- Exactly one active (pending-grace or executing) request per user.
CREATE UNIQUE NONCLUSTERED INDEX [UX_AccountDeletionRequests_ActiveUser]
ON [dbo].[AccountDeletionRequests] ([UserId])
WHERE [Status] IN (1, 3);
GO

-- Worker scan: due requests in grace-end order.
CREATE NONCLUSTERED INDEX [IX_AccountDeletionRequests_Status_GraceEndsAtUtc]
ON [dbo].[AccountDeletionRequests] ([Status], [GraceEndsAtUtc]);
GO

CREATE NONCLUSTERED INDEX [IX_AccountDeletionRequests_UserId]
ON [dbo].[AccountDeletionRequests] ([UserId], [RequestedAtUtc] DESC);
GO
