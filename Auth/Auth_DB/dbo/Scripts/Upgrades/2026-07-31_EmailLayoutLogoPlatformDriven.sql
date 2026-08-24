/*
  2026-07-31 - Email layout logo becomes platform-driven

  The seeded global email layout carried a hard-coded brand logo URL
  (https://company.com/branding/company-email-logo.png). The renderer now
  exposes {{ Platform.LogoUrl }} (PlatformSettings.LogoUrl composed to an
  absolute URL), so the layout must render the configured logo - or fall
  back to a text wordmark when no logo is set.

  Idempotent: rewrites only rows that still contain the old literal block,
  in both DraftContent and PublishedContent. Fresh databases get the new
  block straight from SeedData\11_NotificationLayouts.sql and this script
  is a no-op there. Layouts an admin already customized away from the old
  literal are left untouched.
*/
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

PRINT 'Upgrade 2026-07-31: pointing the email layout logo at Platform.LogoUrl...';

DECLARE @OldLogo NVARCHAR(MAX) = N'<tr><td class="logo"><img src="https://company.com/branding/company-email-logo.png" width="200" alt="{{ Platform.Name }}"></td></tr>';
DECLARE @NewLogo NVARCHAR(MAX) = N'<tr><td class="logo">{% if Platform.LogoUrl %}<img src="{{ Platform.LogoUrl }}" width="200" alt="{{ Platform.Name }}">{% else %}<div class="header" style="padding:0;"><h1>{{ Platform.Name }}</h1></div>{% endif %}</td></tr>';

UPDATE [dbo].[NotificationLayouts]
SET [DraftContent] = REPLACE([DraftContent], @OldLogo, @NewLogo)
WHERE CHARINDEX(@OldLogo, [DraftContent]) > 0;

PRINT '  DraftContent rows updated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

UPDATE [dbo].[NotificationLayouts]
SET [PublishedContent] = REPLACE([PublishedContent], @OldLogo, @NewLogo)
WHERE CHARINDEX(@OldLogo, [PublishedContent]) > 0;

PRINT '  PublishedContent rows updated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
GO
