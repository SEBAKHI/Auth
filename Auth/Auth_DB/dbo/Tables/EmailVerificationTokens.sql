-- =============================================
-- Table: EmailVerificationTokens
-- Description: Stores one-time passwords for email verification
-- =============================================
CREATE TABLE [dbo].[EmailVerificationTokens] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Otp] NVARCHAR(10) NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_EmailVerificationTokens_CreatedAt DEFAULT GETUTCDATE(),
    [IsUsed] BIT NOT NULL CONSTRAINT DF_EmailVerificationTokens_IsUsed DEFAULT 0,
    [UsedAt] DATETIME2(7) NULL,
    CONSTRAINT PK_EmailVerificationTokens PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT FK_EmailVerificationTokens_Users FOREIGN KEY ([UserId]) 
        REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
);
GO

-- =============================================
-- Indexes for EmailVerificationTokens Table
-- =============================================
CREATE NONCLUSTERED INDEX IX_EmailVerificationTokens_UserId 
    ON [dbo].[EmailVerificationTokens]([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX IX_EmailVerificationTokens_ExpiresAt 
    ON [dbo].[EmailVerificationTokens]([ExpiresAt] ASC);
GO

CREATE NONCLUSTERED INDEX IX_EmailVerificationTokens_Otp 
    ON [dbo].[EmailVerificationTokens]([Otp] ASC);
GO