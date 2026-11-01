CREATE TABLE [dbo].[LoginDesigns]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_LoginDesigns_Id] DEFAULT NEWID(),
    -- NULL = one of the platform's designs; a value = one of that application's.
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [Name] NVARCHAR(200) NOT NULL,
    -- The panel half of the sign-in pages: free HTML and CSS with Liquid placeholders
    -- ({{ App.Name }}, {{ Platform.Name }}, {{ lang }}, {{ dir }}, {{ strings.* | raw }}).
    -- At most 64 KB of UTF-8, checked on save (LoginDesign.ContentTooLarge).
    [Content] NVARCHAR(MAX) NOT NULL,
    -- Per-language strings: JSON object { "<lang>": { "<key>": "..." } }, as a
    -- notification layout keeps its chrome strings.
    [StringsJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_LoginDesigns_StringsJson] DEFAULT N'{}',
    -- 0 = the design hides the platform's mark, and only it: the corner stays empty and the
    -- application's logo stays above the form.
    [ShowPlatformLogo] BIT NOT NULL CONSTRAINT [DF_LoginDesigns_ShowPlatformLogo] DEFAULT 1,
    -- No draft and no publish: saving an active design changes what visitors see.
    -- A new design and a copy start inactive.
    [IsActive] BIT NOT NULL CONSTRAINT [DF_LoginDesigns_IsActive] DEFAULT 0,
    -- SHA-256 (lower-case hex) of the content and the strings: the version the sign-in
    -- pages key their cached copy on.
    [ContentHash] CHAR(64) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_LoginDesigns_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_LoginDesigns] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_LoginDesigns_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [CK_LoginDesigns_StringsJson] CHECK (ISJSON([StringsJson]) = 1)
);
GO

-- At most one active design per application, and one for the platform: a unique index
-- treats NULLs as equal, so the platform's designs (ApplicationId NULL) share one slot.
-- Activation swaps the active design in one transaction; two concurrent activations in
-- one scope cannot both commit, the loser gets a unique violation (409).
-- A filtered index needs QUOTED_IDENTIFIER ON for every statement that writes this
-- table: SqlClient sets it, a sqlcmd script must say SET QUOTED_IDENTIFIER ON.
CREATE UNIQUE NONCLUSTERED INDEX [UX_LoginDesigns_ActivePerScope]
    ON [dbo].[LoginDesigns] ([ApplicationId])
    WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_LoginDesigns_ApplicationId]
    ON [dbo].[LoginDesigns] ([ApplicationId]);
GO
