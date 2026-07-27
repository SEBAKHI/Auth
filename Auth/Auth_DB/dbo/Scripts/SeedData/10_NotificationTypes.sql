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

-- ownership-transfer-code (OTP sent to the prospective new owner)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000005',
        N'ownership-transfer-code',
        N'Ownership Transfer Code',
        N'One-time code emailed to the prospective new owner to confirm an organization ownership transfer',
        1,
        N'[{"name":"TargetName","description":"Prospective new owner display name (recipient)","example":"Jane Doe","required":true},{"name":"OwnerName","description":"Current owner display name (initiator)","example":"John Smith","required":true},{"name":"OrganizationName","description":"Name of the organization being transferred","example":"Acme Inc","required":true},{"name":"OtpCode","description":"6-digit confirmation code","example":"123456","required":true},{"name":"ExpirationMinutes","description":"Minutes until the code expires","example":"15","required":true}]',
        N'{"TargetName":"Jane Doe","OwnerName":"John Smith","OrganizationName":"Acme Inc","OtpCode":"123456","ExpirationMinutes":15}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created ownership-transfer-code notification type';
END

-- ownership-transferred (post-transfer notice to both previous and new owner)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000006')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000006',
        N'ownership-transferred',
        N'Ownership Transferred',
        N'Notice sent to both the previous and the new owner after an organization ownership transfer completes',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"OrganizationName","description":"Name of the transferred organization","example":"Acme Inc","required":true},{"name":"PreviousOwnerName","description":"Display name of the previous owner","example":"John Smith","required":true},{"name":"NewOwnerName","description":"Display name of the new owner","example":"Jane Doe","required":true}]',
        N'{"UserName":"Jane Doe","OrganizationName":"Acme Inc","PreviousOwnerName":"John Smith","NewOwnerName":"Jane Doe"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created ownership-transferred notification type';
END

-- account-deletion-requested (grace acknowledgment with the recovery link)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000007')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000007',
        N'account-deletion-requested',
        N'Account Deletion Requested',
        N'Acknowledgment sent when a user requests account deletion: the grace deadline and how to recover the account',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"GraceEndsAt","description":"UTC timestamp when the recovery window closes","example":"2026-08-26 12:00:00Z","required":true},{"name":"GraceDays","description":"Length of the recovery window in days","example":"30","required":true},{"name":"RecoveryLink","description":"Absolute URL of the account recovery page","example":"https://example.com/account-recovery","required":true}]',
        N'{"UserName":"Jane Doe","GraceEndsAt":"2026-08-26 12:00:00Z","GraceDays":30,"RecoveryLink":"https://example.com/account-recovery"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created account-deletion-requested notification type';
END

-- account-deletion-verification (OTP confirming a deletion request)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000008')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000008',
        N'account-deletion-verification',
        N'Account Deletion Verification',
        N'One-time code confirming an account deletion request (passwordless in-app and the public no-login flow)',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"OtpCode","description":"6-digit verification code","example":"123456","required":true},{"name":"ExpirationMinutes","description":"Minutes until the code expires","example":"15","required":true}]',
        N'{"UserName":"Jane Doe","OtpCode":"123456","ExpirationMinutes":15}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created account-deletion-verification notification type';
END

-- account-deletion-cancelled (recovery confirmation; doubles as a security signal)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000009')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000009',
        N'account-deletion-cancelled',
        N'Account Deletion Cancelled',
        N'Confirmation sent when a pending account deletion is cancelled and the account is restored',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"CancelledAt","description":"UTC timestamp when the deletion was cancelled","example":"2026-08-10 09:30:00Z","required":true}]',
        N'{"UserName":"Jane Doe","CancelledAt":"2026-08-10 09:30:00Z"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created account-deletion-cancelled notification type';
END

-- account-deletion-completed (final notice to the pre-destruction snapshot address)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000010')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000010',
        N'account-deletion-completed',
        N'Account Deletion Completed',
        N'Final confirmation that the account and its personal data were permanently deleted',
        1,
        N'[{"name":"UserName","description":"Recipient display name (pre-destruction snapshot)","example":"Jane Doe","required":true}]',
        N'{"UserName":"Jane Doe"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created account-deletion-completed notification type';
END
GO
