-- Retires every live passkey bound to the RP ID example.com.
-- Before you run this: announce the move, and ask people to add another way to sign in.
-- After it: set Passkeys:RpId to the new domain, then recycle the API's application pool.
-- The count it prints is the number of passkeys retired; their audit rows say rp-id-move.
SET XACT_ABORT ON; BEGIN TRANSACTION;
IF OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NULL THROW 50002, N'No passkey table: nothing to retire', 1;
IF OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NOT NULL
BEGIN
    DECLARE @RevokedPasskeys TABLE ([Id] UNIQUEIDENTIFIER, [UserId] UNIQUEIDENTIFIER, [Name] NVARCHAR(128));
    UPDATE [dbo].[UserPasskeys] SET [RevokedAt] = SYSUTCDATETIME(), [RevokedReason] = N'rp-id-move', [RevokedBy] = NULL
     OUTPUT inserted.[Id], inserted.[UserId], inserted.[Name] INTO @RevokedPasskeys
     WHERE [RpId] = N'example.com' AND [RevokedAt] IS NULL;
    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [EntityType], [EntityId], [Details], [ActionType], [IsSuccess])
    SELECT r.[UserId], N'passkey.removed', N'UserPasskey', r.[Id],
           (SELECT r.[Name] AS [name], N'rp-id-move' AS [reason] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           N'Security', 1
    FROM @RevokedPasskeys r;
    SELECT COUNT(*) AS [PasskeysRevoked] FROM @RevokedPasskeys;
END;
COMMIT;
