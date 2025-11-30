CREATE TABLE [dbo].[Roles]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Roles_Id DEFAULT NEWID(),
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT 1,

    -- Auditing Fields
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [UpdatedAt] DATETIME2(7) NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,

    CONSTRAINT PK_Roles PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT UQ_Roles_Name UNIQUE NONCLUSTERED ([Name] ASC)
);

GO

-- =============================================
-- Indexes for Roles Table
-- =============================================
CREATE NONCLUSTERED INDEX IX_Roles_IsActive 
  ON [dbo].[Roles]([IsActive] ASC);
GO

CREATE NONCLUSTERED INDEX IX_Roles_Name 
    ON [dbo].[Roles]([Name] ASC);
GO