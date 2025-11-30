-- =============================================
-- Table: RefreshTokens
-- Description: Stores refresh tokens for JWT authentication
-- =============================================
CREATE TABLE [dbo].[RefreshTokens] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Token] NVARCHAR(500) NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [CreatedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT GETUTCDATE(),
    [IsRevoked] BIT NOT NULL CONSTRAINT DF_RefreshTokens_IsRevoked DEFAULT 0,
    [RevokedAt] DATETIME2(7) NULL,
    [ReplacedByToken] NVARCHAR(500) NULL,
    [IpAddress] NVARCHAR(50) NULL,
    [UserAgent] NVARCHAR(500) NULL,
    CONSTRAINT PK_RefreshTokens PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT UQ_RefreshTokens_Token UNIQUE NONCLUSTERED ([Token] ASC),
    CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY ([UserId]) 
    REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
);
GO

-- =============================================
-- Indexes for RefreshTokens Table
-- =============================================
CREATE NONCLUSTERED INDEX IX_RefreshTokens_Token 
    ON [dbo].[RefreshTokens]([Token] ASC);
GO

CREATE NONCLUSTERED INDEX IX_RefreshTokens_UserId 
    ON [dbo].[RefreshTokens]([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX IX_RefreshTokens_ExpiresAt 
    ON [dbo].[RefreshTokens]([ExpiresAt] ASC);
GO