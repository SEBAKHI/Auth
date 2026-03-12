-- Migration: Make PasswordHash nullable for external auth users (Google, Apple, etc.)
-- External auth users authenticate via provider tokens and have no local password.
ALTER TABLE [dbo].[Users] ALTER COLUMN [PasswordHash] NVARCHAR(500) NULL;
GO
