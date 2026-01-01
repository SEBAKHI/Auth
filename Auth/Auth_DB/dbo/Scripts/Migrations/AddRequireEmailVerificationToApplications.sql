-- Migration: Add RequireEmailVerification column to Applications table
-- This column controls whether an application requires email verification before login

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Applications]') AND name = 'RequireEmailVerification')
BEGIN
    ALTER TABLE [dbo].[Applications]
    ADD [RequireEmailVerification] BIT NOT NULL DEFAULT 0;

    PRINT 'Added RequireEmailVerification column to Applications table';
END
ELSE
BEGIN
    PRINT 'RequireEmailVerification column already exists';
END
GO
