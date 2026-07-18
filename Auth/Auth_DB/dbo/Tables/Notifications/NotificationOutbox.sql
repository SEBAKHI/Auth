CREATE TABLE [dbo].[NotificationOutbox]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_NotificationOutbox_Id] DEFAULT NEWID(),
    -- Diagnostics: which notification type produced this message.
    [NotificationTypeCode] NVARCHAR(100) NOT NULL,
    -- 1 = Email, 2 = Sms, 3 = Push (NotificationChannelType enum).
    [Channel] TINYINT NOT NULL,
    -- Source application scope (FK: applications are stable, rarely deleted).
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [Recipient] NVARCHAR(500) NOT NULL,
    [RecipientName] NVARCHAR(200) NULL,
    -- Soft reference (no FK): external invitees have no account and deleted
    -- users must not orphan the delivery log.
    [RecipientUserId] UNIQUEIDENTIFIER NULL,
    [LanguageCode] NVARCHAR(10) NOT NULL,
    -- Soft references (no FK): the outbox is an append-only delivery log that
    -- must survive template/version cleanup; a FK would block it.
    [TemplateId] UNIQUEIDENTIFIER NULL,
    [TemplateVersionId] UNIQUEIDENTIFIER NULL,
    -- Human-readable version captured at enqueue (survives version deletion).
    [TemplateVersionNumber] INT NULL,
    -- Content is rendered at enqueue time: template edits never retroactively
    -- change queued mail and the dispatcher needs no template machinery.
    [Subject] NVARCHAR(500) NOT NULL,
    [BodyHtml] NVARCHAR(MAX) NOT NULL,
    [BodyText] NVARCHAR(MAX) NULL,
    -- 0 Pending, 1 Processing, 2 Sent, 3 Retry, 4 Dead.
    [Status] TINYINT NOT NULL CONSTRAINT [DF_NotificationOutbox_Status] DEFAULT 0,
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_NotificationOutbox_AttemptCount] DEFAULT 0,
    [NextAttemptAt] DATETIME2 NOT NULL CONSTRAINT [DF_NotificationOutbox_NextAttemptAt] DEFAULT GETUTCDATE(),
    [ClaimedAt] DATETIME2 NULL,
    [SentAt] DATETIME2 NULL,
    [LastError] NVARCHAR(2000) NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_NotificationOutbox_CreatedAt] DEFAULT GETUTCDATE(),
    -- Actor who triggered the send (Guid 0...001 system, 0...000 self-service flows).
    [CreatedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_NotificationOutbox] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_NotificationOutbox_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [CK_NotificationOutbox_Status] CHECK ([Status] IN (0, 1, 2, 3, 4)),
    CONSTRAINT [CK_NotificationOutbox_Channel] CHECK ([Channel] IN (1, 2, 3))
);
GO

-- Dispatch scan: due Pending/Retry rows in due-time order.
CREATE NONCLUSTERED INDEX [IX_NotificationOutbox_Dispatch]
ON [dbo].[NotificationOutbox] ([Status], [NextAttemptAt])
INCLUDE ([AttemptCount]);
GO

-- Admin log views: newest first per status.
CREATE NONCLUSTERED INDEX [IX_NotificationOutbox_CreatedAt]
ON [dbo].[NotificationOutbox] ([CreatedAt] DESC);
GO
