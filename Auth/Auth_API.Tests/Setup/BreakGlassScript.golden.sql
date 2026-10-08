SET XACT_ABORT ON; BEGIN TRANSACTION;
DECLARE @U UNIQUEIDENTIFIER = (SELECT [Id] FROM [dbo].[Users] WHERE [NormalizedEmail] = N'O''NEIL@EXAMPLE.ORG' AND [IsDeleted] = 0);
IF @U IS NULL THROW 50001, N'No such user', 1;
DELETE FROM [dbo].[TwoFactorAuth] WHERE [UserId] = @U;
UPDATE [dbo].[Users] SET [IsTwoFactorEnabled] = 0 WHERE [Id] = @U;
UPDATE [dbo].[RefreshTokens] SET [RevokedAt] = SYSUTCDATETIME(), [ReasonRevoked] = N'Break-glass two-factor reset'
 WHERE [UserId] = @U AND [RevokedAt] IS NULL;
UPDATE [dbo].[IdpSessions] SET [RevokedAt] = SYSUTCDATETIME() WHERE [UserId] = @U AND [RevokedAt] IS NULL;
UPDATE [dbo].[UserSessions] SET [EndedAt] = SYSUTCDATETIME(), [EndReason] = N'Break-glass two-factor reset'
 WHERE [UserId] = @U AND [EndedAt] IS NULL;
INSERT INTO [dbo].[RevokedTokens] ([RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt])
VALUES (3, LOWER(CONVERT(NVARCHAR(36), @U)), SYSUTCDATETIME(), DATEADD(HOUR, 1, SYSUTCDATETIME()));
COMMIT;
