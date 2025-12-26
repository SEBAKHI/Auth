CREATE TABLE [dbo].[LoginAttempts]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NULL,
    [Username] NVARCHAR(255) NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [IpAddress] NVARCHAR(45) NOT NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [AttemptedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [IsSuccessful] BIT NOT NULL DEFAULT 0,
    [FailureReason] NVARCHAR(100) NULL,

    CONSTRAINT [PK_LoginAttempts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_LoginAttempts_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_LoginAttempts_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- UserId is NULL when user not found
-- Username stores the attempted username/email
-- FailureReason values: 'invalid_password', 'user_not_found', 'account_locked',
--                       '2fa_failed', 'account_inactive', 'email_not_verified'

-- Indexes
CREATE NONCLUSTERED INDEX [IX_LoginAttempts_UserId]
ON [dbo].[LoginAttempts] ([UserId], [AttemptedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_LoginAttempts_IpAddress]
ON [dbo].[LoginAttempts] ([IpAddress], [AttemptedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_LoginAttempts_AttemptedAt]
ON [dbo].[LoginAttempts] ([AttemptedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_LoginAttempts_Username]
ON [dbo].[LoginAttempts] ([Username], [AttemptedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_LoginAttempts_ApplicationId]
ON [dbo].[LoginAttempts] ([ApplicationId], [AttemptedAt] DESC);
GO
