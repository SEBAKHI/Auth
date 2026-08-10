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

-- privacy-policy-updated (material-change notice; sent from the console's policy-versions page)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000011',
        N'privacy-policy-updated',
        N'Privacy Policy Updated',
        N'Notice that the privacy policy changed, sent to every active user before the change takes effect',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"PolicyVersion","description":"New policy version (YYYY.MM)","example":"2026.07","required":true},{"name":"EffectiveDate","description":"Date the new version takes effect (yyyy-MM-dd)","example":"2026-07-28","required":true},{"name":"PolicyLink","description":"Absolute URL of the privacy-policy page","example":"https://example.com/privacy","required":true}]',
        N'{"UserName":"Jane Doe","PolicyVersion":"2026.07","EffectiveDate":"2026-07-28","PolicyLink":"https://example.com/privacy"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created privacy-policy-updated notification type';
END

-- new-device-sign-in (security notice; raised by LoginResponseBuilder when a
-- sign-in arrives from a device signature the user has not been seen on)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000012')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000012',
        N'new-device-sign-in',
        N'New Device Sign-In',
        N'Security notice that an account was signed into from an unrecognised device',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"DeviceName","description":"Browser and operating system, or empty when unrecognised","example":"Chrome on Windows","required":false},{"name":"IpAddress","description":"Address the sign-in came from","example":"203.0.113.42","required":false},{"name":"SignedInAt","description":"When the sign-in happened (UTC, yyyy-MM-dd HH:mm:ssZ)","example":"2026-08-02 14:05:00Z","required":true},{"name":"SecureAccountLink","description":"Absolute URL of the password-reset page","example":"https://example.com/forgot-password","required":true}]',
        N'{"UserName":"Jane Doe","DeviceName":"Chrome on Windows","IpAddress":"203.0.113.42","SignedInAt":"2026-08-02 14:05:00Z","SecureAccountLink":"https://example.com/forgot-password"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created new-device-sign-in notification type';
END

-- account-deleted-by-admin (an administrator destroyed the account; the person
-- did not ask for it, so this is NOT account-deletion-completed, whose copy
-- says "as you requested")
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000013')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000013',
        N'account-deleted-by-admin',
        N'Account Deleted by Administrator',
        N'Notice that an administrator permanently deleted the account and its personal data',
        1,
        N'[]',
        N'{}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created account-deleted-by-admin notification type';
END

-- sessions-revoked-token-reuse (security notice; a spent refresh token was
-- presented a second time, so every token and session the account held was
-- revoked. Distinct from a voluntary "sign out everywhere": the owner did not
-- ask for this, which is exactly why they have to be told)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000014')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000014',
        N'sessions-revoked-token-reuse',
        N'Sessions Revoked After Token Reuse',
        N'Security notice that all sessions were revoked because an already-used refresh token was presented again',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"IpAddress","description":"Address the replayed token arrived from, or an em dash when unavailable","example":"203.0.113.42","required":false},{"name":"DetectedAt","description":"When the replay was seen (UTC, yyyy-MM-dd HH:mm:ssZ)","example":"2026-08-05 09:14:00Z","required":true},{"name":"SecureAccountLink","description":"Absolute URL of the password-reset page. MUST NOT be a link that restores a session: this notice is sent at the one moment the account may already be under someone else''s control","example":"https://example.com/forgot-password","required":true}]',
        N'{"UserName":"Jane Doe","IpAddress":"203.0.113.42","DetectedAt":"2026-08-05 09:14:00Z","SecureAccountLink":"https://example.com/forgot-password"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created sessions-revoked-token-reuse notification type';
END

-- session-limit-enforced (a sign-in put the account past
-- Session:MaxConcurrentSessions, so its least recently used sessions were
-- ended. NOT sessions-revoked-token-reuse: nothing is suspected here, this is
-- ordinary policy, and copy that implies a breach would be a false alarm. The
-- owner is told anyway — a device dropping out of a signed-in account with no
-- explanation looks exactly like a hijacking)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000015')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000015',
        N'session-limit-enforced',
        N'Signed Out by the Session Limit',
        N'Notice that older sessions were ended because the account reached its concurrent session limit',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"EndedCount","description":"How many sessions were ended","example":"1","required":true},{"name":"EndedDevices","description":"Comma-separated labels of the ended sessions; an em dash stands in for any that could not be named","example":"Safari on iPhone, Firefox on Ubuntu","required":true},{"name":"NewDeviceName","description":"The device that just signed in and triggered the limit, or an em dash when unrecognised","example":"Chrome on Windows","required":false},{"name":"SessionLimit","description":"The configured maximum number of concurrent sessions","example":"5","required":true},{"name":"SignedOutAt","description":"When the sessions were ended (UTC, yyyy-MM-dd HH:mm:ssZ)","example":"2026-08-10 11:42:00Z","required":true},{"name":"ManageSessionsLink","description":"Absolute URL of the profile page where the user can review their sessions. MUST NOT be a one-click action link: mail scanners prefetch links, so anything that ended a session would fire before a human read it","example":"https://example.com/profile","required":true}]',
        N'{"UserName":"Jane Doe","EndedCount":"1","EndedDevices":"Safari on iPhone","NewDeviceName":"Chrome on Windows","SessionLimit":"5","SignedOutAt":"2026-08-10 11:42:00Z","ManageSessionsLink":"https://example.com/profile"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created session-limit-enforced notification type';
END

-- secret-operation-challenge (OTP gating the destructive platform key
-- operations. The operation being confirmed is a declared variable, not
-- flavour text: a reader who cannot tell a signing-key rotation from a
-- gateway-token reset cannot tell a request they made from one they did not.
-- IpAddress and RequestedAt are required for the same reason)
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000016')
BEGIN
    INSERT INTO [dbo].[NotificationTypes] ([Id], [Code], [Name], [Description], [IsSystem], [VariablesJson], [SampleDataJson], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (
        '40000000-0000-0000-0000-000000000016',
        N'secret-operation-challenge',
        N'Secret Operation Confirmation',
        N'One-time code confirming a destructive platform key operation (regenerate or import the signing key, refresh-token HMAC key, or gateway token)',
        1,
        N'[{"name":"UserName","description":"Recipient display name","example":"Jane Doe","required":true},{"name":"OperationCode","description":"Technical name of the operation being confirmed","example":"GenerateHmacKey","required":true},{"name":"OtpCode","description":"6-digit confirmation code","example":"123456","required":true},{"name":"ExpirationMinutes","description":"Minutes until the code expires","example":"15","required":true},{"name":"IpAddress","description":"Client address the request came from","example":"203.0.113.7","required":true},{"name":"RequestedAt","description":"UTC timestamp of the request","example":"2026-08-10 09:14:00Z","required":true}]',
        N'{"UserName":"Jane Doe","OperationCode":"GenerateHmacKey","OtpCode":"123456","ExpirationMinutes":15,"IpAddress":"203.0.113.7","RequestedAt":"2026-08-10 09:14:00Z"}',
        1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created secret-operation-challenge notification type';
END
GO
