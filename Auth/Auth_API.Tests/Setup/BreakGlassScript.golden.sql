-- Before you run this: is the second factor hard-locked (too many wrong codes)?
-- If FailedAttempts below is at or above Password:ConsecutiveFailureLimit (100
-- by default), whoever drove it there knew the password. Run this only after
-- the account's owner completed a password reset by email LATER than
-- ModifiedAt (LastPasswordChangeUtc > ModifiedAt), and you confirmed it is the
-- owner outside email. The check reads and changes nothing:
-- SELECT t.[FailedAttempts], t.[ModifiedAt], u.[LastPasswordChangeUtc] FROM [dbo].[Users] u LEFT JOIN [dbo].[TwoFactorAuth] t ON t.[UserId] = u.[Id] WHERE u.[NormalizedEmail] = N'O''NEIL@EXAMPLE.ORG' AND u.[IsDeleted] = 0;
SET XACT_ABORT ON; BEGIN TRANSACTION;
DECLARE @U UNIQUEIDENTIFIER = (SELECT [Id] FROM [dbo].[Users] WHERE [NormalizedEmail] = N'O''NEIL@EXAMPLE.ORG' AND [IsDeleted] = 0);
IF @U IS NULL THROW 50001, N'No such user', 1;
DELETE FROM [dbo].[TwoFactorAuth] WHERE [UserId] = @U;
UPDATE [dbo].[Users] SET [IsTwoFactorEnabled] = 0 WHERE [Id] = @U;
IF OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NOT NULL
BEGIN
    DECLARE @RevokedPasskeys TABLE ([Id] UNIQUEIDENTIFIER, [UserId] UNIQUEIDENTIFIER, [Name] NVARCHAR(128));
    UPDATE [dbo].[UserPasskeys] SET [RevokedAt] = SYSUTCDATETIME(), [RevokedReason] = N'break-glass', [RevokedBy] = NULL
     OUTPUT inserted.[Id], inserted.[UserId], inserted.[Name] INTO @RevokedPasskeys
     WHERE [UserId] = @U AND [RevokedAt] IS NULL;
    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [EntityType], [EntityId], [Details], [ActionType], [IsSuccess])
    SELECT r.[UserId], N'passkey.removed', N'UserPasskey', r.[Id],
           (SELECT r.[Name] AS [name], N'break-glass' AS [reason] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           N'Security', 1
    FROM @RevokedPasskeys r;
    SELECT COUNT(*) AS [PasskeysRevoked] FROM @RevokedPasskeys;
END;
UPDATE [dbo].[RefreshTokens] SET [RevokedAt] = SYSUTCDATETIME(), [ReasonRevoked] = N'Break-glass two-factor reset'
 WHERE [UserId] = @U AND [RevokedAt] IS NULL;
UPDATE [dbo].[IdpSessions] SET [RevokedAt] = SYSUTCDATETIME() WHERE [UserId] = @U AND [RevokedAt] IS NULL;
UPDATE [dbo].[UserSessions] SET [EndedAt] = SYSUTCDATETIME(), [EndReason] = N'Break-glass two-factor reset'
 WHERE [UserId] = @U AND [EndedAt] IS NULL;
INSERT INTO [dbo].[RevokedTokens] ([RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt])
VALUES (3, LOWER(CONVERT(NVARCHAR(36), @U)), SYSUTCDATETIME(), DATEADD(MINUTE, 1445, SYSUTCDATETIME()));
COMMIT;
