/*
  2026-08-23 - Email layout: the footer shares the card's surface

  The footer sat on its own tint (#FAFAF9 light, #17171A dark) inside a white card. Nothing
  else in the message changes colour mid-card, so the band read as a second, unexplained
  surface stacked under the message rather than as the end of it. The border-top already
  divides the two, and it is enough on its own.

  The footer now takes the card's colour exactly: #FFFFFF light, #1A1A1C dark - the same
  values .card, .logo, .application, .brand-rule and .content already carry, in the plain
  rule, in the prefers-color-scheme block, in the Outlook [data-ogsb] block, and in the
  bgcolor attribute that style-stripping clients fall back to.

  The declaration is NEVER dropped, only repointed. An absent background leaves a
  partial-inverting client (Gmail) no colour to convert, so it paints its own behind the
  footer text while the card keeps its light palette - the mismatched-surface failure the
  frame comment in the seed describes at length.

  Three single-line replacements rather than a whole-layout rewrite: this changes colour and
  nothing else, so a targeted edit reaches layouts an admin has customised elsewhere in the
  document, which a fingerprint-gated rewrite would have to skip. Each replacement is its own
  complete CSS declaration - no newlines - so it is immune to whether a given database stored
  the layout with CRLF or LF endings.

  Idempotent: once run, none of the four search literals exist, and both the light and the
  Outlook rule already read #1A1A1C in the dark blocks.

  ORDER IS LOAD-BEARING even though nothing here is fingerprinted. Both 2026-08-10 scripts
  overwrite the whole layout column with a frozen literal that still carries #FAFAF9/#17171A,
  so this must be included AFTER them in Script.PostDeployment.sql. Run before either, it
  applies the fix and then has it overwritten inside the same deploy, and the log reads as a
  success. EmailLayoutContractTests.UpgradeChain_RunsInOrderInPostDeployment guards the
  position.

  Fresh databases get the new colours straight from SeedData\11_NotificationLayouts.sql and
  this script is a no-op there. The two are held byte-identical by
  EmailLayoutContractTests.SeedAndUpgrade_CarryByteIdenticalLayout, which replays the @Old*/
  @New* pairs below over the 2026-08-10 RTL literal - so those DECLARE names are part of the
  contract, not local variables.
*/
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

PRINT 'Upgrade 2026-08-23: giving the email footer the card''s own surface...';

DECLARE @OldLight  NVARCHAR(100) = N'.footer { background-color:#FAFAF9;';
DECLARE @NewLight  NVARCHAR(100) = N'.footer { background-color:#FFFFFF;';
DECLARE @OldDark   NVARCHAR(100) = N'.footer { background-color:#17171A !important;';
DECLARE @NewDark   NVARCHAR(100) = N'.footer { background-color:#1A1A1C !important;';
DECLARE @OldOgsb   NVARCHAR(100) = N'[data-ogsb] .footer { background-color:#17171A !important; }';
DECLARE @NewOgsb   NVARCHAR(100) = N'[data-ogsb] .footer { background-color:#1A1A1C !important; }';
DECLARE @OldAttr   NVARCHAR(100) = N'<td class="footer" bgcolor="#FAFAF9"';
DECLARE @NewAttr   NVARCHAR(100) = N'<td class="footer" bgcolor="#FFFFFF"';

-- Published copy. Channel 1 = Email; the sweep covers application-scoped layouts too.
UPDATE [dbo].[NotificationLayouts]
SET [PublishedContent] =
        REPLACE(REPLACE(REPLACE(REPLACE([PublishedContent],
            @OldLight, @NewLight),
            @OldDark,  @NewDark),
            @OldOgsb,  @NewOgsb),
            @OldAttr,  @NewAttr)
WHERE [Channel] = 1
  AND [PublishedContent] IS NOT NULL
  AND (CHARINDEX(@OldLight, [PublishedContent]) > 0
    OR CHARINDEX(@OldDark,  [PublishedContent]) > 0
    OR CHARINDEX(@OldOgsb,  [PublishedContent]) > 0
    OR CHARINDEX(@OldAttr,  [PublishedContent]) > 0);

PRINT '  PublishedContent rows updated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

-- Draft copy, kept in step so the console shows no phantom "unpublished changes" diff.
UPDATE [dbo].[NotificationLayouts]
SET [DraftContent] =
        REPLACE(REPLACE(REPLACE(REPLACE([DraftContent],
            @OldLight, @NewLight),
            @OldDark,  @NewDark),
            @OldOgsb,  @NewOgsb),
            @OldAttr,  @NewAttr)
WHERE [Channel] = 1
  AND (CHARINDEX(@OldLight, [DraftContent]) > 0
    OR CHARINDEX(@OldDark,  [DraftContent]) > 0
    OR CHARINDEX(@OldOgsb,  [DraftContent]) > 0
    OR CHARINDEX(@OldAttr,  [DraftContent]) > 0);

PRINT '  DraftContent rows updated: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));

-- Report, never touch, any Email layout that ended up without the new light declaration:
-- one whose footer rule was rewritten by hand, and which therefore keeps whatever surface
-- its author chose. Nothing here is broken; it is simply outside this script's reach.
DECLARE @Customised INT = (
    SELECT COUNT(*) FROM [dbo].[NotificationLayouts]
    WHERE [Channel] = 1 AND CHARINDEX(@NewLight, ISNULL([PublishedContent], N'')) = 0);

IF @Customised > 0
BEGIN
    PRINT '  NOTE: ' + CAST(@Customised AS NVARCHAR(10)) +
          ' Email layout(s) carry a hand-written footer rule and were left untouched.';
    SELECT [Id], [ApplicationId], [Name]
    FROM [dbo].[NotificationLayouts]
    WHERE [Channel] = 1 AND CHARINDEX(@NewLight, ISNULL([PublishedContent], N'')) = 0;
END

PRINT '  Note: sends use the new layout within 15 minutes (TemplateCache absolute TTL).';
GO
