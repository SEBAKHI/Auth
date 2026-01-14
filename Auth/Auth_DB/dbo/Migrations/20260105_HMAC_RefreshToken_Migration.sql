/*
================================================================================
Migration: HMAC-SHA256 Refresh Token Security Upgrade
Date: 2026-01-05
Description: Converts refresh token storage from plain text + Argon2id hash
             to HMAC-SHA256 hash only for enhanced security.

             IMPORTANT: This migration will invalidate ALL existing refresh tokens.
             All users will need to re-login after this migration is applied.
================================================================================
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

BEGIN TRY
    PRINT 'Starting HMAC-SHA256 refresh token migration...';

    -- Step 1: Revoke all existing refresh tokens
    -- Reason: Existing tokens used Argon2id hash format which is incompatible with HMAC-SHA256
    PRINT 'Step 1: Revoking all existing refresh tokens...';

    UPDATE [dbo].[RefreshTokens]
    SET [RevokedAt] = GETUTCDATE(),
        [ReasonRevoked] = 'Security upgrade: HMAC-SHA256 migration'
    WHERE [RevokedAt] IS NULL;

    PRINT CONCAT('  Revoked ', @@ROWCOUNT, ' active refresh tokens.');

    -- Step 2: Drop the index on Token column (if exists)
    PRINT 'Step 2: Dropping Token index...';

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshTokens_Token'
               AND object_id = OBJECT_ID(N'[dbo].[RefreshTokens]'))
    BEGIN
        DROP INDEX [IX_RefreshTokens_Token] ON [dbo].[RefreshTokens];
        PRINT '  Dropped IX_RefreshTokens_Token index.';
    END
    ELSE
    BEGIN
        PRINT '  IX_RefreshTokens_Token index does not exist, skipping.';
    END

    -- Step 3: Drop the Token column (if exists)
    PRINT 'Step 3: Dropping Token column...';

    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[RefreshTokens]') AND name = 'Token')
    BEGIN
        ALTER TABLE [dbo].[RefreshTokens] DROP COLUMN [Token];
        PRINT '  Dropped Token column.';
    END
    ELSE
    BEGIN
        PRINT '  Token column does not exist, skipping.';
    END

    -- Step 4: Rename ReplacedByToken to ReplacedByTokenHash
    PRINT 'Step 4: Renaming ReplacedByToken to ReplacedByTokenHash...';

    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[RefreshTokens]') AND name = 'ReplacedByToken')
    BEGIN
        EXEC sp_rename 'dbo.RefreshTokens.ReplacedByToken', 'ReplacedByTokenHash', 'COLUMN';
        PRINT '  Renamed ReplacedByToken to ReplacedByTokenHash.';
    END
    ELSE IF EXISTS (SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[dbo].[RefreshTokens]') AND name = 'ReplacedByTokenHash')
    BEGIN
        PRINT '  ReplacedByTokenHash column already exists, skipping rename.';
    END
    ELSE
    BEGIN
        -- Column doesn't exist at all, create it
        ALTER TABLE [dbo].[RefreshTokens] ADD [ReplacedByTokenHash] NVARCHAR(100) NULL;
        PRINT '  Created ReplacedByTokenHash column.';
    END

    -- Step 5: Resize TokenHash column if needed (100 is sufficient for HMAC-SHA256 base64)
    PRINT 'Step 5: Adjusting TokenHash column size...';

    DECLARE @CurrentLength INT;
    SELECT @CurrentLength = max_length / 2  -- NVARCHAR stores 2 bytes per character
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[RefreshTokens]') AND name = 'TokenHash';

    IF @CurrentLength > 100
    BEGIN
        -- Need to drop and recreate index before modifying column
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshTokens_TokenHash'
                   AND object_id = OBJECT_ID(N'[dbo].[RefreshTokens]'))
        BEGIN
            DROP INDEX [IX_RefreshTokens_TokenHash] ON [dbo].[RefreshTokens];
            PRINT '  Dropped IX_RefreshTokens_TokenHash index for column resize.';
        END

        ALTER TABLE [dbo].[RefreshTokens] ALTER COLUMN [TokenHash] NVARCHAR(100) NOT NULL;
        PRINT CONCAT('  Resized TokenHash from NVARCHAR(', @CurrentLength, ') to NVARCHAR(100).');
    END
    ELSE
    BEGIN
        PRINT CONCAT('  TokenHash column size (', @CurrentLength, ') is already appropriate.');
    END

    -- Step 6: Create/recreate unique index on TokenHash
    PRINT 'Step 6: Creating unique index on TokenHash...';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshTokens_TokenHash'
                   AND object_id = OBJECT_ID(N'[dbo].[RefreshTokens]'))
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX [IX_RefreshTokens_TokenHash]
        ON [dbo].[RefreshTokens] ([TokenHash]);
        PRINT '  Created unique index IX_RefreshTokens_TokenHash.';
    END
    ELSE
    BEGIN
        PRINT '  IX_RefreshTokens_TokenHash index already exists.';
    END

    -- Step 7: Update stored procedures
    PRINT 'Step 7: Updating stored procedures...';

    -- Drop and recreate sp_CreateRefreshToken
    IF OBJECT_ID('[dbo].[sp_CreateRefreshToken]', 'P') IS NOT NULL
    BEGIN
        DROP PROCEDURE [dbo].[sp_CreateRefreshToken];
    END

    EXEC('
    CREATE PROCEDURE [dbo].[sp_CreateRefreshToken]
        @UserId UNIQUEIDENTIFIER,
        @TokenHash NVARCHAR(100),
        @JwtId NVARCHAR(100),
        @ApplicationId UNIQUEIDENTIFIER = NULL,
        @DeviceInfo NVARCHAR(500) = NULL,
        @IpAddress NVARCHAR(45) = NULL,
        @ExpiresAt DATETIME2
    AS
    BEGIN
        SET NOCOUNT ON;

        DECLARE @TokenId UNIQUEIDENTIFIER = NEWID();

        INSERT INTO [dbo].[RefreshTokens]
        (
            [Id],
            [UserId],
            [TokenHash],
            [JwtId],
            [ApplicationId],
            [DeviceInfo],
            [IpAddress],
            [CreatedAt],
            [ExpiresAt]
        )
        VALUES
        (
            @TokenId,
            @UserId,
            @TokenHash,
            @JwtId,
            @ApplicationId,
            @DeviceInfo,
            @IpAddress,
            GETUTCDATE(),
            @ExpiresAt
        );

        SELECT @TokenId AS [TokenId];
    END
    ');
    PRINT '  Updated sp_CreateRefreshToken.';

    -- Drop and recreate sp_RevokeRefreshToken
    IF OBJECT_ID('[dbo].[sp_RevokeRefreshToken]', 'P') IS NOT NULL
    BEGIN
        DROP PROCEDURE [dbo].[sp_RevokeRefreshToken];
    END

    EXEC('
    CREATE PROCEDURE [dbo].[sp_RevokeRefreshToken]
        @TokenHash NVARCHAR(100),
        @RevokedBy UNIQUEIDENTIFIER = NULL,
        @ReasonRevoked NVARCHAR(200) = NULL,
        @ReplacedByTokenHash NVARCHAR(100) = NULL
    AS
    BEGIN
        SET NOCOUNT ON;

        DECLARE @TokenId UNIQUEIDENTIFIER;
        DECLARE @AlreadyRevoked BIT = 0;

        SELECT
            @TokenId = [Id],
            @AlreadyRevoked = CASE WHEN [RevokedAt] IS NOT NULL THEN 1 ELSE 0 END
        FROM [dbo].[RefreshTokens]
        WHERE [TokenHash] = @TokenHash;

        IF @TokenId IS NULL
        BEGIN
            SELECT 0 AS [Success], N''Token not found'' AS [Message];
            RETURN;
        END

        IF @AlreadyRevoked = 1
        BEGIN
            SELECT 0 AS [Success], N''Token already revoked'' AS [Message];
            RETURN;
        END

        UPDATE [dbo].[RefreshTokens]
        SET [RevokedAt] = GETUTCDATE(),
            [RevokedBy] = @RevokedBy,
            [ReasonRevoked] = @ReasonRevoked,
            [ReplacedByTokenHash] = @ReplacedByTokenHash
        WHERE [Id] = @TokenId;

        SELECT 1 AS [Success], N''Token revoked successfully'' AS [Message];
    END
    ');
    PRINT '  Updated sp_RevokeRefreshToken.';

    -- Drop and recreate sp_ValidateRefreshToken
    IF OBJECT_ID('[dbo].[sp_ValidateRefreshToken]', 'P') IS NOT NULL
    BEGIN
        DROP PROCEDURE [dbo].[sp_ValidateRefreshToken];
    END

    EXEC('
    CREATE PROCEDURE [dbo].[sp_ValidateRefreshToken]
        @TokenHash NVARCHAR(100)
    AS
    BEGIN
        SET NOCOUNT ON;

        SELECT
            rt.[Id],
            rt.[UserId],
            rt.[TokenHash],
            rt.[JwtId],
            rt.[ApplicationId],
            rt.[DeviceInfo],
            rt.[IpAddress],
            rt.[CreatedAt],
            rt.[ExpiresAt],
            rt.[RevokedAt],
            rt.[RevokedBy],
            rt.[ReplacedByTokenHash],
            rt.[ReasonRevoked],
            CASE
                WHEN rt.[RevokedAt] IS NOT NULL THEN 0
                WHEN rt.[ExpiresAt] < GETUTCDATE() THEN 0
                ELSE 1
            END AS [IsValid],
            CASE
                WHEN rt.[ExpiresAt] < GETUTCDATE() THEN 1
                ELSE 0
            END AS [IsExpired],
            CASE
                WHEN rt.[RevokedAt] IS NOT NULL THEN 1
                ELSE 0
            END AS [IsRevoked],
            u.[Status] AS [UserStatus],
            u.[IsDeleted] AS [UserIsDeleted]
        FROM [dbo].[RefreshTokens] rt
        INNER JOIN [dbo].[Users] u ON rt.[UserId] = u.[Id]
        WHERE rt.[TokenHash] = @TokenHash;
    END
    ');
    PRINT '  Updated sp_ValidateRefreshToken.';

    COMMIT TRANSACTION;
    PRINT '';
    PRINT '================================================================================';
    PRINT 'Migration completed successfully!';
    PRINT '';
    PRINT 'IMPORTANT: All existing refresh tokens have been revoked.';
    PRINT 'Users will need to re-login to obtain new tokens.';
    PRINT '================================================================================';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT '';
    PRINT '================================================================================';
    PRINT 'Migration FAILED!';
    PRINT '';
    PRINT CONCAT('Error Number: ', ERROR_NUMBER());
    PRINT CONCAT('Error Message: ', ERROR_MESSAGE());
    PRINT CONCAT('Error Line: ', ERROR_LINE());
    PRINT '================================================================================';

    THROW;
END CATCH
GO
