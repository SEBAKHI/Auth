-- Seed external auth providers
IF NOT EXISTS (SELECT 1 FROM [dbo].[ExternalAuthProviders] WHERE [Code] = N'google')
BEGIN
    INSERT INTO [dbo].[ExternalAuthProviders] ([Code], [Name], [IconUrl], [IsEnabled], [DisplayOrder])
    VALUES (N'google', N'Google', N'https://www.gstatic.com/firebasejs/ui/2.0.0/images/auth/google.svg', 1, 1);
    PRINT 'Created Google external auth provider';
END
ELSE
BEGIN
    PRINT 'Google external auth provider already exists';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[ExternalAuthProviders] WHERE [Code] = N'apple')
BEGIN
    -- Seeded DISABLED: operations enable this row only after the Apple Services
    -- ID, domain verification and .p8 signing key are provisioned. IconUrl is
    -- NULL because the SPA renders Apple's official button, not an icon.
    INSERT INTO [dbo].[ExternalAuthProviders] ([Code], [Name], [IconUrl], [IsEnabled], [DisplayOrder])
    VALUES (N'apple', N'Apple', NULL, 0, 2);
    PRINT 'Created Apple external auth provider (disabled)';
END
ELSE
BEGIN
    PRINT 'Apple external auth provider already exists';
END
