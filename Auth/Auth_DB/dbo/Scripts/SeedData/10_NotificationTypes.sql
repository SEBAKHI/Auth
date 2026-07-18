-- Notification Types Seed Data
-- System notification types with their variable catalogs and preview sample data.
-- The variable catalog is the contract between calling code and templates; the renderer
-- additionally injects SenderName (from Email settings) into every render model.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- email-verification (OTP)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000001',
        N'email-verification',
        N'Email Verification',
        N'One-time code sent to confirm a user''s email address',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"OtpCode","description":"6-digit verification code","example":"123456","required":true},{"name":"ExpirationMinutes","description":"Minutes until the code expires","example":"15","required":true}]',
        N'{"UserName":"Jane Doe","OtpCode":"123456","ExpirationMinutes":15}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created email-verification notification type';
END

-- password-reset (link)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000002',
        N'password-reset',
        N'Password Reset',
        N'Link-only password reset email',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"ResetLink","description":"Absolute URL of the reset-password page including the token","example":"https://example.com/reset-password?token=SAMPLE","required":true},{"name":"ExpirationMinutes","description":"Minutes until the link expires","example":"60","required":true}]',
        N'{"UserName":"Jane Doe","ResetLink":"https://example.com/reset-password?token=SAMPLE","ExpirationMinutes":60}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created password-reset notification type';
END

-- organization-invitation (link + token)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000003')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000003',
        N'organization-invitation',
        N'Organization Invitation',
        N'Invitation to join an organization, sent to the invitee''s email',
        1,
        N'[{"name":"OrganizationName","description":"Name of the inviting organization","example":"Acme Inc","required":true},{"name":"InviterName","description":"Display name of the user who sent the invitation","example":"John Smith","required":true},{"name":"InvitationLink","description":"Absolute URL of the accept-invitation page including the token","example":"https://example.com/accept-invitation?token=SAMPLE","required":true},{"name":"InvitationToken","description":"Plaintext invitation token for manual entry","example":"SAMPLE-INVITE-TOKEN","required":true},{"name":"ExpiresAt","description":"UTC timestamp when the invitation expires","example":"2026-12-31T23:59:00Z","required":true}]',
        N'{"OrganizationName":"Acme Inc","InviterName":"John Smith","InvitationLink":"https://example.com/accept-invitation?token=SAMPLE","InvitationToken":"SAMPLE-INVITE-TOKEN","ExpiresAt":"2026-12-31T23:59:00Z"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created organization-invitation notification type';
END

-- welcome-email (type only; no template is seeded or published yet)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000004')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000004',
        N'welcome-email',
        N'Welcome Email',
        N'Optional welcome message after successful registration',
        0,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true}]',
        N'{"UserName":"Jane Doe"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created welcome-email notification type';
END
GO
