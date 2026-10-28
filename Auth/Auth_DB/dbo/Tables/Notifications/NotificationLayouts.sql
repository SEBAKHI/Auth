CREATE TABLE [dbo].[NotificationLayouts]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_NotificationLayouts_Id] DEFAULT NEWID(),
    -- NULL = the global default layout; a value = an application-specific layout.
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    -- 1 = Email, 2 = Sms, 3 = Push (NotificationChannelType enum).
    [Channel] TINYINT NOT NULL CONSTRAINT [DF_NotificationLayouts_Channel] DEFAULT 1,
    [Name] NVARCHAR(200) NOT NULL,
    -- Full Liquid HTML document. Placeholders: {{ content | raw }}, {{ dir }}, {{ lang }},
    -- {{ strings.* | raw }} (per-language chrome strings), {{ SenderName }}.
    [DraftContent] NVARCHAR(MAX) NOT NULL,
    -- Per-language chrome strings: JSON object { "<lang>": { "footer": "..." } }.
    [DraftStringsJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_NotificationLayouts_DraftStringsJson] DEFAULT N'{}',
    -- NULL = never published. Publish copies the Draft columns here in one atomic UPDATE.
    [PublishedContent] NVARCHAR(MAX) NULL,
    [PublishedStringsJson] NVARCHAR(MAX) NULL,
    [PublishedAt] DATETIME2 NULL,
    [PublishedBy] UNIQUEIDENTIFIER NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_NotificationLayouts_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    -- Card colours (#RRGGBB, upper case; NULL = the default #FFFFFF / #1A1A1C). The email
    -- logo's background is baked into the PNG in this colour, so it must equal the card
    -- behind the logo. Draft/published pair like the content, copied by the same publish.
    -- Declared LAST so DacFx adds them with ALTER TABLE instead of rebuilding the table.
    [DraftCardColorLight] NVARCHAR(7) NULL,
    [DraftCardColorDark] NVARCHAR(7) NULL,
    [PublishedCardColorLight] NVARCHAR(7) NULL,
    [PublishedCardColorDark] NVARCHAR(7) NULL,

    CONSTRAINT [PK_NotificationLayouts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_NotificationLayouts_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [CK_NotificationLayouts_Channel] CHECK ([Channel] IN (1, 2, 3)),
    CONSTRAINT [CK_NotificationLayouts_DraftStringsJson] CHECK (ISJSON([DraftStringsJson]) = 1),
    CONSTRAINT [CK_NotificationLayouts_PublishedStringsJson] CHECK ([PublishedStringsJson] IS NULL OR ISJSON([PublishedStringsJson]) = 1),
    CONSTRAINT [CK_NotificationLayouts_DraftCardColorLight] CHECK ([DraftCardColorLight] IS NULL OR [DraftCardColorLight] LIKE N'#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CONSTRAINT [CK_NotificationLayouts_DraftCardColorDark] CHECK ([DraftCardColorDark] IS NULL OR [DraftCardColorDark] LIKE N'#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CONSTRAINT [CK_NotificationLayouts_PublishedCardColorLight] CHECK ([PublishedCardColorLight] IS NULL OR [PublishedCardColorLight] LIKE N'#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    CONSTRAINT [CK_NotificationLayouts_PublishedCardColorDark] CHECK ([PublishedCardColorDark] IS NULL OR [PublishedCardColorDark] LIKE N'#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
    -- One layout per (application, channel) scope; SQL Server treats NULLs as equal in
    -- unique constraints, so exactly one global layout per channel is allowed.
    CONSTRAINT [UQ_NotificationLayouts_App_Channel] UNIQUE ([ApplicationId], [Channel])
);
GO
