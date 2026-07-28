CREATE TABLE [dbo].[PrivacyPolicyVersions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PrivacyPolicyVersions_Id] DEFAULT NEWID(),
    [Version] NVARCHAR(20) NOT NULL,           -- "YYYY.MM"; mirrors AccountDeletionSettings.PolicyVersion
    [EffectiveDateUtc] DATETIME2 NOT NULL,
    [NotifiedAtUtc] DATETIME2 NULL,            -- when the change notice went out; NULL = not yet sent
    [NotifiedCount] INT NULL,                  -- how many active users the notice reached
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PrivacyPolicyVersions_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    -- Declared last: new columns append without a table rebuild on publish.
    [IsPublished] BIT NOT NULL CONSTRAINT [DF_PrivacyPolicyVersions_IsPublished] DEFAULT 0,

    CONSTRAINT [PK_PrivacyPolicyVersions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_PrivacyPolicyVersions_Version] UNIQUE ([Version])
);
GO

-- Privacy-policy revision registry: the compliance record of when each policy
-- version took effect and when (and to how many recipients) the change notice
-- was sent. Rows are permanent — legal evidence, never swept.
