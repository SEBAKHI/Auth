CREATE TABLE [dbo].[SystemSettingsOverrides]
(
    [SectionKey]    NVARCHAR(64)     NOT NULL,
    [OverridesJson] NVARCHAR(MAX)    NOT NULL CONSTRAINT [DF_SystemSettingsOverrides_OverridesJson] DEFAULT N'{}',
    [Version]       INT              NOT NULL CONSTRAINT [DF_SystemSettingsOverrides_Version] DEFAULT 1,
    [ModifiedAt]    DATETIME2        NOT NULL,
    [ModifiedBy]    UNIQUEIDENTIFIER NULL,
    [RowVersion]    ROWVERSION       NOT NULL,
    CONSTRAINT [PK_SystemSettingsOverrides] PRIMARY KEY CLUSTERED ([SectionKey]),
    CONSTRAINT [CK_SystemSettingsOverrides_ValidJson] CHECK (ISJSON([OverridesJson]) = 1)
);
