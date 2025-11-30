-- =============================================
-- Table: LoginAttempts
-- Description: Logs all login attempts for security auditing
-- =============================================
CREATE TABLE [dbo].[LoginAttempts] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Email] NVARCHAR(256) NOT NULL,
    [IsSuccessful] BIT NOT NULL CONSTRAINT DF_LoginAttempts_IsSuccessful DEFAULT 0,
    [IpAddress] NVARCHAR(50) NOT NULL,
    [UserAgent] NVARCHAR(500) NOT NULL,
    [AttemptedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_LoginAttempts_AttemptedAt DEFAULT GETUTCDATE(),
    [FailureReason] NVARCHAR(500) NULL,
    CONSTRAINT PK_LoginAttempts PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

-- =============================================
-- Indexes for LoginAttempts Table
-- =============================================
CREATE NONCLUSTERED INDEX IX_LoginAttempts_Email 
    ON [dbo].[LoginAttempts]([Email] ASC);
GO

CREATE NONCLUSTERED INDEX IX_LoginAttempts_AttemptedAt 
    ON [dbo].[LoginAttempts]([AttemptedAt] DESC);
GO

CREATE NONCLUSTERED INDEX IX_LoginAttempts_IsSuccessful 
    ON [dbo].[LoginAttempts]([IsSuccessful] ASC);
GO