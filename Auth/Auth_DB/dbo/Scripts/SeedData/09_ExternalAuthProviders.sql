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
