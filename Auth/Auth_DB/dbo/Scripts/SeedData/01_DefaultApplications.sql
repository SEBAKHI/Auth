-- Default Applications Seed Data
-- This script creates the Auth System as the first application

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Insert Auth System application (if not exists)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [Id] = @AuthAppId)
BEGIN
    INSERT INTO [dbo].[Applications]
    (
        [Id],
        [Code],
        [Name],
        [Description],
        [BaseUrl],
        [IsActive],
        [AllowSelfRegistration],
        [RequireTwoFactor],
        [SessionTimeoutMinutes],
        [MaxConcurrentSessions],
        [CreatedAt],
        [CreatedBy]
    )
    VALUES
    (
        @AuthAppId,
        N'auth',
        N'Auth System',
        N'Central Authentication and Authorization System',
        N'https://auth.company.com',
        1,
        0,  -- No self-registration for Auth System
        0,  -- 2FA optional
        60, -- 60 minute session timeout
        10, -- Max 10 concurrent sessions
        GETUTCDATE(),
        @SystemUserId
    );

    PRINT 'Created Auth System application';
END
ELSE
BEGIN
    PRINT 'Auth System application already exists';
END
GO
