-- Privacy Policy Versions Seed Data
-- Registers the initial policy revision (mirrors AccountDeletionSettings.PolicyVersion
-- and the frontend POLICY_VERSION constant). Idempotent.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[PrivacyPolicyVersions] WHERE [Version] = N'2026.07')
BEGIN
    INSERT INTO [dbo].[PrivacyPolicyVersions]
        ([Id], [Version], [EffectiveDateUtc], [NotifiedAtUtc], [NotifiedCount], [CreatedAt], [CreatedBy])
    VALUES
        ('46000000-0000-0000-0000-000000000001', N'2026.07', '2026-07-28T00:00:00', NULL, NULL, GETUTCDATE(), @SystemUserId);
    PRINT 'Created privacy policy version 2026.07';
END
