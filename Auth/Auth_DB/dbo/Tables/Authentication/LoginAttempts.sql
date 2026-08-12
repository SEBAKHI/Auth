CREATE TABLE [dbo].[LoginAttempts]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_LoginAttempts_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NULL,
    [Username] NVARCHAR(255) NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [IpAddress] NVARCHAR(45) NOT NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [AttemptedAt] DATETIME2 NOT NULL CONSTRAINT [DF_LoginAttempts_AttemptedAt] DEFAULT GETUTCDATE(),
    [IsSuccessful] BIT NOT NULL CONSTRAINT [DF_LoginAttempts_IsSuccessful] DEFAULT 0,
    [FailureReason] NVARCHAR(100) NULL,
    [TwoFactorChallengeId] UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_LoginAttempts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_LoginAttempts_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_LoginAttempts_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

-- UserId is NULL when user not found.
-- Username stores the attempted username/email.
--
-- ONE ROW PER SIGN-IN CEREMONY, not per HTTP request. A two-factor sign-in spans
-- two requests: the row is opened when the challenge is issued and closed when the
-- second factor settles it. The three states are encoded in columns that already
-- existed, which is why there is no separate status column:
--
--   IsSuccessful | FailureReason | TwoFactorChallengeId | meaning
--   -------------+---------------+----------------------+------------------------------
--        0       |     NULL      |        <id>          | ceremony open. Read as "not
--                |               |                      | completed" once older than
--                |               |                      | the challenge lifetime.
--        1       |     NULL      |        <id>          | second factor accepted
--        0       |    '...'      |        <id>          | ceremony settled as a failure
--        1       |     NULL      |        NULL          | sign-in with no second factor
--        0       |    '...'      |        NULL          | rejected before any challenge
--
-- IsSuccessful = 0 AND FailureReason IS NULL is otherwise unreachable (the failure
-- factory demands a reason), which is what makes it a safe marker for "open". The
-- natural predicate for a real failure is therefore "failed AND has a reason".
--
-- TwoFactorChallengeId is a soft reference to TwoFactorChallenges with NO foreign
-- key on purpose: challenges are purged on a 7-day policy and attempts on a 365-day
-- one, and an account purge deletes the challenges while only anonymising the
-- attempts -- a constraint in either direction would eventually block a delete.
-- The read path LEFT JOINs it for the failed-code count and tolerates a missing row.
--
-- FailureReason holds prose a person reads, not a code. The values the application
-- actually writes are: 'User not found', 'Account locked', 'No password set',
-- 'Invalid password', 'Email not confirmed', 'Too many incorrect verification codes',
-- 'Maximum concurrent sessions reached', and the localized description of a failed
-- account-status check.
-- WARNING: DashboardStatsRepository hardcodes N'Account locked'. Changing that
-- literal silently zeroes the locked-out-attempts metric rather than failing.

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
