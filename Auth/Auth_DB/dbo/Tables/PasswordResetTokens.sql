-- =============================================
-- Table: PasswordResetTokens
-- Description: Stores one-time passwords for password reset
-- =============================================
CREATE TABLE [dbo].[PasswordResetTokens] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Otp] NVARCHAR(10) NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_PasswordResetTokens_CreatedAt DEFAULT GETUTCDATE(),
    [IsUsed] BIT NOT NULL CONSTRAINT DF_PasswordResetTokens_IsUsed DEFAULT 0,
    [UsedAt] DATETIME2(7) NULL,
    CONSTRAINT PK_PasswordResetTokens PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT FK_PasswordResetTokens_Users FOREIGN KEY ([UserId]) 
        REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
);
GO

-- =============================================
-- Indexes for PasswordResetTokens Table
-- =============================================
CREATE NONCLUSTERED INDEX IX_PasswordResetTokens_UserId 
    ON [dbo].[PasswordResetTokens]([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX IX_PasswordResetTokens_ExpiresAt 
    ON [dbo].[PasswordResetTokens]([ExpiresAt] ASC);
GO

CREATE NONCLUSTERED INDEX IX_PasswordResetTokens_Otp 
    ON [dbo].[PasswordResetTokens]([Otp] ASC);
GO