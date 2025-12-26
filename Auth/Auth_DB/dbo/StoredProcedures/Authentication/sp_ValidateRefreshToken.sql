CREATE PROCEDURE [dbo].[sp_ValidateRefreshToken]
    @TokenHash NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        rt.[Id],
        rt.[UserId],
        rt.[Token],
        rt.[TokenHash],
        rt.[JwtId],
        rt.[ApplicationId],
        rt.[DeviceInfo],
        rt.[IpAddress],
        rt.[CreatedAt],
        rt.[ExpiresAt],
        rt.[RevokedAt],
        rt.[RevokedBy],
        rt.[ReplacedByToken],
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
GO
