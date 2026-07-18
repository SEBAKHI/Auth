CREATE TABLE [dbo].[NotificationTypes]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_NotificationTypes_Id] DEFAULT NEWID(),
    [Code] NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    -- System types back critical auth flows; their global templates cannot be unpublished or deleted.
    [IsSystem] BIT NOT NULL CONSTRAINT [DF_NotificationTypes_IsSystem] DEFAULT 0,
    -- Variable catalog: JSON array of { name, description, example, required } objects.
    -- This is the contract between calling code and templates; identical across versions and languages.
    [VariablesJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_NotificationTypes_VariablesJson] DEFAULT N'[]',
    -- Sample values used for admin previews and publish-time validation: JSON object { name: value }.
    [SampleDataJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_NotificationTypes_SampleDataJson] DEFAULT N'{}',
    [IsActive] BIT NOT NULL CONSTRAINT [DF_NotificationTypes_IsActive] DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_NotificationTypes_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_NotificationTypes] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_NotificationTypes_Code] UNIQUE ([Code]),
    CONSTRAINT [CK_NotificationTypes_VariablesJson] CHECK (ISJSON([VariablesJson]) = 1),
    CONSTRAINT [CK_NotificationTypes_SampleDataJson] CHECK (ISJSON([SampleDataJson]) = 1)
);
GO
