-- Notification Layouts Seed Data
-- Global Email layout: SEBAKHI-brand design (monochrome, dark-mode aware, table-based
-- for email-client compatibility). Styles cover both the current template classes and
-- the legacy ones (.warning, .link-fallback with inline links) so older custom
-- templates keep rendering correctly.
-- Liquid placeholders: {{ dir }}, {{ lang }}, {{ content | raw }}, {{ strings.footer | raw }},
-- plus renderer globals ({{ Platform.Name }}, {{ Application.Name }}, {{ Year }}).
-- The per-language chrome strings live in StringsJson; string values are themselves Liquid
-- templates (the renderer resolves {{ SenderName }} before injecting them).

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
/* ============ RESET ============ */
html, body { margin:0 !important; padding:0 !important; width:100% !important; background:transparent; -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
table { border-spacing:0; border-collapse:collapse; }
td { padding:0; }
img { border:0; outline:none; text-decoration:none; display:block; -ms-interpolation-mode:bicubic; }
a { text-decoration:none; }
body, table, td, a, p, div, span {
    font-family: -apple-system, BlinkMacSystemFont, ''Segoe UI'', Roboto, ''Helvetica Neue'', Arial, sans-serif !important;
}

/* ============ FRAME ============ */
/* Everything outside the card is transparent so the email floats on the mail
   client''s own background in both light and dark modes. */
body { background:transparent; color:#1B1B1A; direction:{{ dir }}; text-align:{% if dir == "rtl" %}right{% else %}left{% endif %}; }
.wrapper { background:transparent; padding:40px 16px 24px; }
.card { background:#FFFFFF; border:1px solid #E8E8E6; border-radius:20px; overflow:hidden; }
.top-accent { height:4px; background:#141414; font-size:0; line-height:0; }

/* ============ BRAND HEADER ============ */
/* The logo file is a fully opaque PNG with a white chip baked in: image pixels
   are never recolored by dark modes or Gmail''s proxy, so it stays legible
   everywhere. Corners are rounded via CSS only (keeps the file alpha-free). */
.logo { padding:40px 48px 0; text-align:center; }
.logo img { width:200px; max-width:70%; height:auto; margin:0 auto; border-radius:14px; }
.application { padding:16px 48px 0; text-align:center; font-size:11px; font-weight:600; letter-spacing:2.2px; text-transform:uppercase; color:#9C9C9A; }
.brand-rule { padding:32px 48px 0; }
.brand-rule div { border-top:1px solid #EFEFED; font-size:0; line-height:0; }

/* ============ CONTENT ============ */
.content { padding:36px 48px 44px; }
.header { text-align:center; margin:0 0 28px; }
.eyebrow { margin:0 0 10px; font-size:11px; font-weight:600; letter-spacing:2px; text-transform:uppercase; color:#8C8C8A; }
.header h1 { margin:0; color:#141414; font-size:26px; line-height:1.35; font-weight:700; letter-spacing:-0.2px; }
.subtitle { margin:12px 0 0; color:#757573; font-size:15px; line-height:1.7; }
.message { margin:0 0 16px; color:#3F3F3E; font-size:15px; line-height:1.8; }
.muted { margin:0 0 16px; color:#8C8C8A; font-size:13px; line-height:1.8; }
strong { color:#141414; }

/* ============ BUTTON ============ */
.button-container { text-align:center; margin:30px 0; }
.button { display:inline-block; background:#141414 !important; color:#FFFFFF !important; font-size:15px; font-weight:600; line-height:1; padding:15px 34px; border-radius:12px; letter-spacing:0.2px; }

/* ============ CODES ============ */
.code-container { text-align:center; margin:30px 0; }
.otp-code { display:inline-block; background:#F6F6F5; border:1px solid #E8E8E6; border-radius:14px; padding:20px 28px; font-family:Consolas, ''Courier New'', monospace !important; font-size:32px; font-weight:700; letter-spacing:8px; color:#141414; direction:ltr; }
.token-code { display:inline-block; background:#F6F6F5; border:1px solid #E8E8E6; border-radius:12px; padding:14px 20px; font-family:Consolas, ''Courier New'', monospace !important; font-size:14px; font-weight:600; color:#141414; direction:ltr; word-break:break-all; }

/* ============ LINK FALLBACK ============ */
.link-fallback { margin:0 0 10px; color:#8C8C8A; font-size:13px; line-height:1.7; }
.link-box { margin:0 0 24px; background:#F9F9F8; border:1px solid #EFEFED; border-radius:12px; padding:14px 18px; font-family:Consolas, ''Courier New'', monospace !important; font-size:12px; line-height:1.7; color:#6E6E6C; word-break:break-all; direction:ltr; text-align:left; }
.link-box a { color:#6E6E6C !important; text-decoration:underline; }

/* ============ NOTICE ============ */
.notice, .warning { margin:32px 0 0; background:#F9F9F8; border:1px solid #E8E8E6; border-radius:14px; padding:18px 20px; color:#757573; font-size:13px; line-height:1.8; }
.notice-title { margin:0 0 6px; color:#6E6E6C; font-size:11px; font-weight:700; letter-spacing:1.5px; text-transform:uppercase; }
.notice-text { margin:0; color:#757573; font-size:13px; line-height:1.8; }

/* ============ FOOTER ============ */
.footer { background:#FAFAF9; border-top:1px solid #EFEFED; padding:24px 48px; text-align:center; }
.footer p { margin:0; color:#9C9C9A; font-size:12px; line-height:1.8; }
.subfooter { padding:22px 24px 0; text-align:center; }
/* Mid gray: readable on the client''s own background, whatever its brightness. */
.subfooter p { margin:0; color:#8A8A8C; font-size:12px; line-height:1.7; letter-spacing:0.3px; }

/* ============ DARK MODE ============ */
@media (prefers-color-scheme: dark) {
    .card { background:#1A1A1C !important; border-color:#2C2C2F !important; }
    .top-accent { background:#F4F4F2 !important; }
    .application { color:#8F8F92 !important; }
    .brand-rule div { border-top-color:#28282B !important; }
    .header h1 { color:#F4F4F2 !important; }
    .eyebrow { color:#8F8F92 !important; }
    .subtitle, .message { color:#C9C9C7 !important; }
    .muted, .link-fallback { color:#8F8F92 !important; }
    strong { color:#F4F4F2 !important; }
    .button { background:#F4F4F2 !important; color:#141414 !important; }
    .otp-code, .token-code { background:#202023 !important; border-color:#313134 !important; color:#F4F4F2 !important; }
    .link-box { background:#1E1E21 !important; border-color:#2C2C2F !important; color:#A5A5A3 !important; }
    .link-box a { color:#A5A5A3 !important; }
    .notice, .warning { background:#1E1E21 !important; border-color:#2C2C2F !important; color:#A5A5A3 !important; }
    .notice-title { color:#B8B8B6 !important; }
    .notice-text { color:#A5A5A3 !important; }
    .footer { background:#17171A !important; border-top-color:#28282B !important; }
    .footer p { color:#8F8F92 !important; }
}
/* Outlook.com / Outlook apps dark mode */
[data-ogsb] .card { background:#1A1A1C !important; border-color:#2C2C2F !important; }
[data-ogsb] .button { background:#F4F4F2 !important; }
[data-ogsc] .button { color:#141414 !important; }
[data-ogsb] .otp-code, [data-ogsb] .token-code, [data-ogsb] .link-box, [data-ogsb] .notice, [data-ogsb] .warning { background:#1E1E21 !important; border-color:#2C2C2F !important; }
[data-ogsc] .header h1, [data-ogsc] strong { color:#F4F4F2 !important; }
[data-ogsc] .message, [data-ogsc] .subtitle { color:#C9C9C7 !important; }
[data-ogsc] .otp-code, [data-ogsc] .token-code { color:#F4F4F2 !important; }

/* ============ MOBILE ============ */
@media only screen and (max-width:640px) {
    .wrapper { padding:16px 10px !important; }
    .card { border-radius:16px !important; }
    .logo { padding:30px 24px 0 !important; }
    .logo img { width:176px !important; }
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
<body>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="wrapper">
<tr>
<td align="center">
<table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:600px;">
<tr>
<td>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="card">
<tr><td class="top-accent" style="height:4px;line-height:4px;font-size:2px;">&nbsp;</td></tr>
<tr><td class="logo"><img src="https://astoom.com/branding/sebakhi-email-logo.png" width="200" alt="{{ Platform.Name }}"></td></tr>
<tr><td class="application">{{ Application.Name }}</td></tr>
<tr><td class="brand-rule"><div>&nbsp;</div></td></tr>
<tr><td class="content">
{{ content | raw }}
</td></tr>
<tr><td class="footer"><p>{{ strings.footer | raw }}</p></td></tr>
</table>
</td>
</tr>
<tr>
<td class="subfooter"><p>&copy; {{ Year }} {{ Platform.Name }}</p></td>
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
