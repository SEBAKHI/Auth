-- Notification Layouts Seed Data
-- Global Email layout: COMPANY-brand design (monochrome, 600px table layout, dark-mode aware).
-- Styles cover both the current template classes and the legacy ones (.warning, .link-fallback)
-- so older custom templates keep rendering correctly.
-- Liquid placeholders: {{ dir }}, {{ lang }}, {{ content | raw }}, {{ strings.footer | raw }},
-- plus renderer globals ({{ Platform.Name }}, {{ Application.Name }}, {{ Year }}).
-- The per-language chrome strings live in StringsJson; string values are themselves Liquid
-- templates (the renderer resolves {{ SenderName }} before injecting them).
--
-- DARK MODE, HONESTLY SCOPED (verified against caniemail.com, 2026-08-10):
--   * @media (prefers-color-scheme: dark) reaches Apple Mail 13+, Outlook.com/macOS/iOS/Android,
--     Samsung Email 6.1+, Fastmail. It does NOT reach Gmail on ANY platform, Yahoo, AOL,
--     ProtonMail, Outlook for Windows or HEY.
--   * Gmail instead applies its own transformation (full inversion on iOS, partial on Android)
--     and offers no way to opt in or out. The LIGHT palette is therefore designed to survive
--     being machine-inverted: every surface is opaque, so the client always has a defined
--     colour to convert instead of guessing.
--   * The logo is the one element no client recolours, which is why the light/dark plates are
--     baked into the PNG rather than painted with CSS. See EmailLogoRendition in the API.
--
-- BASE DIRECTION (RTL/LTR) - READ BEFORE EDITING ANY dir= OR direction:
--   Gmail and most webmail do NOT render the email as a document: they strip <html>/<head>,
--   replace <body> with a <div>, and graft the result into their own LTR page. So
--   `<html dir>` never exists there and a `body { direction }` rule in <style> matches
--   nothing - which is exactly why Arabic used to render with an LTR base and throw
--   sentence-final punctuation to the far side of the line.
--   Direction therefore rides on carriers INSIDE <body>, repeated on every container:
--     1. the dir attribute   (caniemail: full support in Gmail web/iOS/Android)
--     2. an inline direction: declaration (survives <style> being dropped entirely)
--     3. align= on the centred cells (survives even inline CSS being stripped)
--   It is deliberately redundant because you cannot predict where a sanitiser cuts.
--   `<html dir>` and the body rule stay: they are the only carriers Outlook for Windows
--   honours, and they cost nothing.
--   NEVER use text-align:start/end here - unsupported in all Outlook for Windows, Yahoo,
--   AOL, Orange, GMX and Web.de. Write right/left through the {{ dir }} conditional.
--   Interpolated identity values sit in <span dir="auto"> so a tenant name ending in a
--   neutral ("Company Inc.") cannot drag the punctuation out of the run.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

DECLARE @LayoutContent NVARCHAR(MAX) = N'<!DOCTYPE html>
<html lang="{{ lang }}" dir="{{ dir }}">
<head>
<meta charset="UTF-8">
<meta http-equiv="X-UA-Compatible" content="IE=edge">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<meta name="color-scheme" content="light dark">
<meta name="supported-color-schemes" content="light dark">
<title>{{ Application.Name }}</title>
<style>
/* ============ COLOR SCHEME ACTIVATION ============ */
/* Apple Mail 13+ applies the dark block ONLY when this CSS property is present; the two meta
   tags above are the Apple Mail 12 spelling and are kept for it alone. Do NOT add
   "supported-color-schemes" as a CSS declaration - no such property exists, and a strict
   sanitiser can drop this whole rule on the parse error, taking Apple Mail dark mode with it. */
:root { color-scheme: light dark; }

/* ============ RESET ============ */
html, body { margin:0 !important; padding:0 !important; width:100% !important; -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
table { border-spacing:0; border-collapse:collapse; }
td { padding:0; }
img { border:0; outline:none; text-decoration:none; display:block; -ms-interpolation-mode:bicubic; }
a { text-decoration:none; }
body, table, td, a, p, div, span {
    font-family: -apple-system, BlinkMacSystemFont, ''Segoe UI'', Roboto, ''Helvetica Neue'', Arial, sans-serif !important;
}

/* ============ FRAME ============ */
/* The frame is OPAQUE on purpose. A transparent surface gives a partial-inverting client no
   colour to convert, so it leaves the area to the client''s own background while the card keeps
   its light palette - mismatched surfaces, which is what "wrong colours in dark mode" looked
   like. It also gives Outlook an ancestor to stamp data-ogsb on; without one, every
   [data-ogsb] rule at the bottom of this sheet is inert. */
html, body { background-color:#F1F1EF; }
body { color:#1B1B1A; direction:{{ dir }}; text-align:{% if dir == "rtl" %}right{% else %}left{% endif %}; }
.wrapper { background-color:#F1F1EF; padding:40px 16px 24px; }
/* The centring cell needs its own colour-only class. It cannot reuse .wrapper (that would
   nest .wrapper''s padding inside itself and double the frame), and it cannot go unclassed:
   it carries an inline background for style-stripping clients, and an inline declaration
   beats an unqualified rule - so without a dark override here the frame stays light around
   a dark card. */
.wrapper-cell { background-color:#F1F1EF; }
.card { background-color:#FFFFFF; border:1px solid #E8E8E6; border-radius:20px; overflow:hidden; }
.top-accent { height:4px; background-color:#141414; font-size:0; line-height:0; }

/* ============ BRAND HEADER ============ */
/* The <img> src is an email rendition: an opaque PNG whose plate is part of the raster. Never
   point this at the raw uploaded logo - uploads are alpha-carrying WebP, which Gmail transcodes
   to JPEG (flattening transparency onto BLACK) and Outlook for Windows cannot decode at all.
   The plate colours are owned by FileSystemImageStorageService and must match .card. */
.logo { background-color:#FFFFFF; padding:40px 48px 0; text-align:center; }
.logo img { max-width:100%; height:auto; margin:0 auto; border-radius:14px; }
.logo-dark { display:none; }
.application { background-color:#FFFFFF; padding:16px 48px 0; text-align:center; font-size:11px; font-weight:600; letter-spacing:2.2px; text-transform:uppercase; color:#8C8C8A; }
.brand-rule { background-color:#FFFFFF; padding:32px 48px 0; }
.brand-rule div { border-top:1px solid #EFEFED; font-size:0; line-height:0; }

/* ============ CONTENT ============ */
.content { background-color:#FFFFFF; padding:36px 48px 44px; }
.header { text-align:center; margin:0 0 28px; }
.eyebrow { margin:0 0 10px; font-size:11px; font-weight:600; letter-spacing:2px; text-transform:uppercase; color:#7A7A78; }
.header h1 { margin:0; color:#141414; font-size:26px; line-height:1.35; font-weight:700; letter-spacing:-0.2px; }
.subtitle { margin:12px 0 0; color:#5F5F5D; font-size:15px; line-height:1.7; }
.message { margin:0 0 16px; color:#3F3F3E; font-size:15px; line-height:1.8; }
.muted { margin:0 0 16px; color:#7A7A78; font-size:13px; line-height:1.8; }
strong { color:#141414; }

/* ============ BUTTON ============ */
.button-container { text-align:center; margin:30px 0; }
.button { display:inline-block; background-color:#141414 !important; color:#FFFFFF !important; font-size:15px; font-weight:600; line-height:1; padding:15px 34px; border-radius:12px; letter-spacing:0.2px; }

/* ============ CODES ============ */
.code-container { text-align:center; margin:30px 0; }
.otp-code { display:inline-block; background-color:#F6F6F5; border:1px solid #E8E8E6; border-radius:14px; padding:20px 28px; font-family:Consolas, ''Courier New'', monospace !important; font-size:32px; font-weight:700; letter-spacing:8px; color:#141414; direction:ltr; }
.token-code { display:inline-block; background-color:#F6F6F5; border:1px solid #E8E8E6; border-radius:12px; padding:14px 20px; font-family:Consolas, ''Courier New'', monospace !important; font-size:14px; font-weight:600; color:#141414; direction:ltr; word-break:break-all; }

/* ============ LINK FALLBACK ============ */
.link-fallback { margin:0 0 10px; color:#7A7A78; font-size:13px; line-height:1.7; }
/* direction:ltr keeps the URL''s ":" "/" "?" "=" neutrals in order. text-align is written as an
   ABSOLUTE keyword: the flow-relative values start/end are unsupported in every Outlook for
   Windows, Yahoo, AOL, Orange, GMX and Web.de. Since this rule pins direction:ltr, "start"
   resolved to "left" anyway - so this is portability at zero visual cost. */
.link-box { margin:0 0 24px; background-color:#F9F9F8; border:1px solid #EFEFED; border-radius:12px; padding:14px 18px; font-family:Consolas, ''Courier New'', monospace !important; font-size:12px; line-height:1.7; color:#5F5F5D; word-break:break-all; direction:ltr; text-align:left; }
.link-box a { color:#5F5F5D !important; text-decoration:underline; }

/* ============ NOTICE ============ */
.notice, .warning { margin:32px 0 0; background-color:#F9F9F8; border:1px solid #E8E8E6; border-radius:14px; padding:18px 20px; color:#5F5F5D; font-size:13px; line-height:1.8; }
.notice-title { margin:0 0 6px; color:#4A4A48; font-size:11px; font-weight:700; letter-spacing:1.5px; text-transform:uppercase; }
.notice-text { margin:0; color:#5F5F5D; font-size:13px; line-height:1.8; }

/* ============ FOOTER ============ */
.footer { background-color:#FAFAF9; border-top:1px solid #EFEFED; padding:24px 48px; text-align:center; }
.footer p { margin:0; color:#8C8C8A; font-size:12px; line-height:1.8; }
.subfooter { background-color:#F1F1EF; padding:22px 24px 0; text-align:center; }
.subfooter p { margin:0; color:#6E6E6C; font-size:12px; line-height:1.7; letter-spacing:0.3px; }

/* ============ DARK MODE ============ */
/* Every colour-bearing rule above has an entry here. A partial list is worse than none: it
   darkens some surfaces and leaves others light, which reads as a broken email. */
@media (prefers-color-scheme: dark) {
    html, body { background-color:#0E0E10 !important; }
    body { color:#C9C9C7 !important; }
    .wrapper, .wrapper-cell { background-color:#0E0E10 !important; }
    .card { background-color:#1A1A1C !important; border-color:#2C2C2F !important; }
    .top-accent { background-color:#F4F4F2 !important; }
    .logo, .application, .brand-rule, .content { background-color:#1A1A1C !important; }
    .application { color:#8F8F92 !important; }
    .brand-rule div { border-top-color:#28282B !important; }
    .header h1 { color:#F4F4F2 !important; }
    .eyebrow { color:#8F8F92 !important; }
    .subtitle, .message { color:#C9C9C7 !important; }
    .muted, .link-fallback { color:#8F8F92 !important; }
    strong { color:#F4F4F2 !important; }
    .button { background-color:#F4F4F2 !important; color:#141414 !important; }
    .otp-code, .token-code { background-color:#202023 !important; border-color:#313134 !important; color:#F4F4F2 !important; }
    .link-box { background-color:#1E1E21 !important; border-color:#2C2C2F !important; color:#A5A5A3 !important; }
    .link-box a { color:#A5A5A3 !important; }
    .notice, .warning { background-color:#1E1E21 !important; border-color:#2C2C2F !important; color:#A5A5A3 !important; }
    .notice-title { color:#B8B8B6 !important; }
    .notice-text { color:#A5A5A3 !important; }
    .footer { background-color:#17171A !important; border-top-color:#28282B !important; }
    .footer p { color:#8F8F92 !important; }
    .subfooter { background-color:#0E0E10 !important; }
    .subfooter p { color:#8F8F92 !important; }
    /* Swap to the dark-plated logo only when one was actually configured. Without the
       has-dark gate an unset dark logo would leave the light chip on a dark card - still
       legible, which is the point of the gate. */
    .logo.has-dark .logo-light { display:none !important; }
    .logo.has-dark .logo-dark { display:block !important; }
}

/* ============ OUTLOOK.COM / OUTLOOK APPS ============ */
/* Outlook recolours an element and records the original in data-ogsb (background) or
   data-ogsc (colour) ON THAT ELEMENT, so only the DESCENDANT form works. A class+attribute
   selector such as .card[data-ogsb] is unsupported, and comma-joining one here would
   invalidate the entire rule - including the descendant half that does work.
   Best-effort by design: Outlook injects its own inline !important declarations, which no
   <style> rule can outrank on the elements it chose to repaint. */
[data-ogsb] .card, [data-ogsb] .logo, [data-ogsb] .application, [data-ogsb] .brand-rule, [data-ogsb] .content { background-color:#1A1A1C !important; }
[data-ogsb] .footer { background-color:#17171A !important; }
[data-ogsb] .wrapper, [data-ogsb] .wrapper-cell, [data-ogsb] .subfooter { background-color:#0E0E10 !important; }
[data-ogsb] .top-accent { background-color:#F4F4F2 !important; }
[data-ogsb] .button { background-color:#F4F4F2 !important; }
[data-ogsb] .otp-code, [data-ogsb] .token-code, [data-ogsb] .link-box, [data-ogsb] .notice, [data-ogsb] .warning { background-color:#1E1E21 !important; border-color:#2C2C2F !important; }
[data-ogsb] .brand-rule div { border-top-color:#28282B !important; }
[data-ogsc] .button { color:#141414 !important; }
[data-ogsc] .header h1, [data-ogsc] strong { color:#F4F4F2 !important; }
[data-ogsc] .message, [data-ogsc] .subtitle { color:#C9C9C7 !important; }
[data-ogsc] .eyebrow, [data-ogsc] .application, [data-ogsc] .muted, [data-ogsc] .link-fallback, [data-ogsc] .footer p, [data-ogsc] .subfooter p { color:#8F8F92 !important; }
[data-ogsc] .notice-title { color:#B8B8B6 !important; }
[data-ogsc] .notice, [data-ogsc] .warning, [data-ogsc] .notice-text, [data-ogsc] .link-box, [data-ogsc] .link-box a { color:#A5A5A3 !important; }
[data-ogsc] .otp-code, [data-ogsc] .token-code { color:#F4F4F2 !important; }
[data-ogsc] .logo.has-dark .logo-light { display:none !important; }
[data-ogsc] .logo.has-dark .logo-dark { display:block !important; }

/* ============ MOBILE ============ */
@media only screen and (max-width:640px) {
    .wrapper { padding:16px 10px !important; }
    .card { border-radius:16px !important; }
    .logo { padding:30px 24px 0 !important; }
    .application { padding:14px 24px 0 !important; }
    .brand-rule { padding:26px 24px 0 !important; }
    .content { padding:28px 24px 34px !important; }
    .header h1 { font-size:22px !important; }
    .message { font-size:14px !important; }
    .button { display:block !important; width:100% !important; box-sizing:border-box; }
    .otp-code { font-size:26px !important; letter-spacing:6px !important; padding:16px 20px !important; }
    .footer { padding:20px 24px !important; }
}
</style>
</head>
<body bgcolor="#F1F1EF" dir="{{ dir }}" style="background-color:#F1F1EF;direction:{{ dir }};">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="wrapper" bgcolor="#F1F1EF" dir="{{ dir }}" style="direction:{{ dir }};">
<tr>
<td class="wrapper-cell" align="center" bgcolor="#F1F1EF" dir="{{ dir }}" style="background-color:#F1F1EF;direction:{{ dir }};">
<table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" dir="{{ dir }}" style="width:100%;max-width:600px;direction:{{ dir }};">
<tr>
<td dir="{{ dir }}" style="direction:{{ dir }};">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="card" bgcolor="#FFFFFF" dir="{{ dir }}" style="direction:{{ dir }};">
<tr><td class="top-accent" bgcolor="#141414" style="height:4px;line-height:4px;font-size:2px;">&nbsp;</td></tr>
<tr><td class="logo{% if Platform.EmailLogoDarkUrl %} has-dark{% endif %}" bgcolor="#FFFFFF">{% if Platform.EmailLogoUrl %}<img class="logo-light" src="{{ Platform.EmailLogoUrl }}"{% if Platform.EmailLogoWidth %} width="{{ Platform.EmailLogoWidth }}" height="{{ Platform.EmailLogoHeight }}"{% endif %} alt="{{ Platform.Name }}">{% if Platform.EmailLogoDarkUrl %}<!--[if !mso]><!--><img class="logo-dark" src="{{ Platform.EmailLogoDarkUrl }}"{% if Platform.EmailLogoDarkWidth %} width="{{ Platform.EmailLogoDarkWidth }}" height="{{ Platform.EmailLogoDarkHeight }}"{% endif %} alt="{{ Platform.Name }}" style="display:none;"><!--<![endif]-->{% endif %}{% else %}<div class="header" style="padding:0;text-align:center;"><h1 dir="auto">{{ Platform.Name }}</h1></div>{% endif %}</td></tr>
<tr><td class="application" bgcolor="#FFFFFF" align="center" dir="{{ dir }}" style="direction:{{ dir }};text-align:center;"><span dir="auto">{{ Application.Name }}</span></td></tr>
<tr><td class="brand-rule" bgcolor="#FFFFFF"><div>&nbsp;</div></td></tr>
<tr><td class="content" bgcolor="#FFFFFF" dir="{{ dir }}" style="direction:{{ dir }};">
<div dir="{{ dir }}" style="direction:{{ dir }};text-align:{% if dir == ''rtl'' %}right{% else %}left{% endif %};">
{{ content | raw }}
</div>
</td></tr>
<tr><td class="footer" bgcolor="#FAFAF9" align="center" dir="{{ dir }}" style="direction:{{ dir }};text-align:center;"><p dir="{{ dir }}" style="direction:{{ dir }};text-align:center;">{{ strings.footer | raw }}</p></td></tr>
</table>
</td>
</tr>
<tr>
<td class="subfooter" bgcolor="#F1F1EF" align="center" dir="{{ dir }}" style="direction:{{ dir }};text-align:center;"><p dir="{{ dir }}" style="direction:{{ dir }};text-align:center;">&copy; {{ Year }} <span dir="auto">{{ Platform.Name }}</span></p></td>
</tr>
</table>
</td>
</tr>
</table>
</body>
</html>';

DECLARE @LayoutStrings NVARCHAR(MAX) = N'{
"en": {"footer": "This is an automated message from {{ SenderName }}. Please do not reply to this email."},
"ar": {"footer": "هذه رسالة تلقائية من {{ SenderName }}. يرجى عدم الرد على هذا البريد الإلكتروني."},
"tr": {"footer": "Bu, {{ SenderName }} tarafından gönderilen otomatik bir mesajdır. Lütfen bu e-postayı yanıtlamayın."},
"fr": {"footer": "Ceci est un message automatique de {{ SenderName }}. Veuillez ne pas répondre à cet e-mail."},
"zh": {"footer": "这是来自{{ SenderName }}的自动消息，请勿回复此邮件。"},
"ur": {"footer": "یہ {{ SenderName }} کی طرف سے ایک خودکار پیغام ہے۔ براہ کرم اس ای میل کا جواب نہ دیں۔"},
"fa": {"footer": "این یک پیام خودکار از {{ SenderName }} است. لطفاً به این ایمیل پاسخ ندهید."}
}';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationLayouts] WHERE [Id] = '41000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[NotificationLayouts]
        ([Id], [ApplicationId], [Channel], [Name], [DraftContent], [DraftStringsJson], [PublishedContent], [PublishedStringsJson], [PublishedAt], [PublishedBy], [CreatedAt], [CreatedBy])
    VALUES
        ('41000000-0000-0000-0000-000000000001', NULL, 1, N'Default Email Layout',
         @LayoutContent, @LayoutStrings, @LayoutContent, @LayoutStrings,
         GETUTCDATE(), @SystemUserId, GETUTCDATE(), @SystemUserId);
    PRINT 'Created default global email layout (published)';
END
ELSE
BEGIN
    PRINT 'Default email layout already exists';
END
GO
