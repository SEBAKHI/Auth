-- SEBAKHI email redesign upgrade for EXISTING databases (dev/prod).
-- Fresh databases get this design from the seed scripts; this script upgrades DBs where
-- the guarded seeds already ran. Idempotent: guarded by the new version ids.
--
-- 1. Overwrites the global email layout (draft + published) with the new design.
-- 2. Adds a new published version (max + 1) to each of the three system templates,
--    keeping all previous versions in the history (roll back from the console if needed).

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

UPDATE [dbo].[NotificationLayouts]
SET [DraftContent] = @LayoutContent,
    [DraftStringsJson] = @LayoutStrings,
    [PublishedContent] = @LayoutContent,
    [PublishedStringsJson] = @LayoutStrings,
    [PublishedAt] = GETUTCDATE(),
    [PublishedBy] = @SystemUserId,
    [ModifiedAt] = GETUTCDATE(),
    [ModifiedBy] = @SystemUserId
WHERE [Id] = '41000000-0000-0000-0000-000000000001';

PRINT 'Global email layout updated (draft + published)';
GO

-- ============================================================
-- email-verification: new published version in the SEBAKHI-brand design
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000001')
AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateVersions] WHERE [Id] = '43000000-0000-0000-0000-000000000101')
BEGIN
    DECLARE @NextVersion INT =
        (SELECT ISNULL(MAX([VersionNumber]), 0) + 1 FROM [dbo].[NotificationTemplateVersions] WHERE [TemplateId] = '42000000-0000-0000-0000-000000000001');

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000101', '42000000-0000-0000-0000-000000000001', @NextVersion, N'SEBAKHI-brand redesign', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    (NEWID(), '43000000-0000-0000-0000-000000000101', N'en', N'Verify Your Email Address',
N'<div class="header">
    <p class="eyebrow">Email verification</p>
    <h1>Confirm your email address</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">Enter the code below to confirm your email address. It expires in {{ ExpirationMinutes }} minutes.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Security notice</p>
    <p class="notice-text">If you did not request this code, you can safely ignore this email. Never share this code with anyone — {{ Platform.Name }} will never ask you for it.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000101', N'ar', N'تأكيد عنوان بريدك الإلكتروني',
N'<div class="header">
    <p class="eyebrow">التحقق من البريد الإلكتروني</p>
    <h1>تأكيد عنوان بريدك الإلكتروني</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">أدخل الرمز أدناه لتأكيد عنوان بريدك الإلكتروني. تنتهي صلاحيته خلال {{ ExpirationMinutes }} دقيقة.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">تنبيه أمني</p>
    <p class="notice-text">إذا لم تطلب هذا الرمز، يمكنك تجاهل هذا البريد الإلكتروني بأمان. لا تشارك هذا الرمز مع أي شخص — لن يطلبه منك فريق {{ Platform.Name }} أبدًا.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000101', N'tr', N'E-posta Adresinizi Doğrulayın',
N'<div class="header">
    <p class="eyebrow">E-posta doğrulama</p>
    <h1>E-posta adresinizi onaylayın</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">E-posta adresinizi onaylamak için aşağıdaki kodu girin. Kodun süresi {{ ExpirationMinutes }} dakika içinde dolacaktır.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Güvenlik uyarısı</p>
    <p class="notice-text">Bu kodu siz talep etmediyseniz bu e-postayı güvenle yok sayabilirsiniz. Bu kodu asla kimseyle paylaşmayın — {{ Platform.Name }} bu kodu sizden hiçbir zaman istemez.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000101', N'fr', N'Vérifiez votre adresse e-mail',
N'<div class="header">
    <p class="eyebrow">Vérification de l''e-mail</p>
    <h1>Confirmez votre adresse e-mail</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Saisissez le code ci-dessous pour confirmer votre adresse e-mail. Il expirera dans {{ ExpirationMinutes }} minutes.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Avis de sécurité</p>
    <p class="notice-text">Si vous n''avez pas demandé ce code, vous pouvez ignorer cet e-mail en toute sécurité. Ne partagez jamais ce code — {{ Platform.Name }} ne vous le demandera jamais.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000101', N'zh', N'验证您的邮箱地址',
N'<div class="header">
    <p class="eyebrow">邮箱验证</p>
    <h1>确认您的邮箱地址</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">请输入以下验证码以确认您的邮箱地址。验证码将在 {{ ExpirationMinutes }} 分钟后失效。</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">安全提示</p>
    <p class="notice-text">如果您并未请求此验证码，请放心忽略此邮件。请勿与任何人分享此验证码 — {{ Platform.Name }} 绝不会向您索取。</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000101', N'ur', N'اپنا ای میل ایڈریس تصدیق کریں',
N'<div class="header">
    <p class="eyebrow">ای میل کی تصدیق</p>
    <h1>اپنے ای میل ایڈریس کی تصدیق کریں</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">اپنے ای میل ایڈریس کی تصدیق کے لیے نیچے دیا گیا کوڈ درج کریں۔ اس کی میعاد {{ ExpirationMinutes }} منٹ میں ختم ہو جائے گی۔</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">حفاظتی نوٹس</p>
    <p class="notice-text">اگر آپ نے یہ کوڈ نہیں مانگا تو آپ اس ای میل کو بحفاظت نظر انداز کر سکتے ہیں۔ یہ کوڈ کبھی کسی کے ساتھ شیئر نہ کریں — {{ Platform.Name }} کبھی آپ سے یہ کوڈ نہیں مانگے گا۔</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000101', N'fa', N'تأیید آدرس ایمیل شما',
N'<div class="header">
    <p class="eyebrow">تأیید ایمیل</p>
    <h1>آدرس ایمیل خود را تأیید کنید</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">برای تأیید آدرس ایمیل خود، کد زیر را وارد کنید. این کد تا {{ ExpirationMinutes }} دقیقه دیگر منقضی می‌شود.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">هشدار امنیتی</p>
    <p class="notice-text">اگر شما این کد را درخواست نکرده‌اید، می‌توانید با خیال راحت این ایمیل را نادیده بگیرید. این کد را هرگز با کسی به اشتراک نگذارید — {{ Platform.Name }} هرگز آن را از شما نمی‌خواهد.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000101',
        [ModifiedAt] = GETUTCDATE(),
        [ModifiedBy] = @SystemUserId
    WHERE [Id] = '42000000-0000-0000-0000-000000000001';

    PRINT 'email-verification: published redesigned version';
END
ELSE
BEGIN
    PRINT 'email-verification: redesign version already applied (or template missing)';
END
GO

-- ============================================================
-- password-reset: new published version in the SEBAKHI-brand design
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000002')
AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateVersions] WHERE [Id] = '43000000-0000-0000-0000-000000000102')
BEGIN
    DECLARE @NextVersion INT =
        (SELECT ISNULL(MAX([VersionNumber]), 0) + 1 FROM [dbo].[NotificationTemplateVersions] WHERE [TemplateId] = '42000000-0000-0000-0000-000000000002');

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000102', '42000000-0000-0000-0000-000000000002', @NextVersion, N'SEBAKHI-brand redesign', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    (NEWID(), '43000000-0000-0000-0000-000000000102', N'en', N'Reset Your Password',
N'<div class="header">
    <p class="eyebrow">Account security</p>
    <h1>Reset your password</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">We received a request to reset the password for your account. Click the button below to choose a new one. This link is valid for {{ ExpirationMinutes }} minutes.</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">Reset password</a>
</div>
<p class="link-fallback">If the button does not work, copy and paste this link into your browser:</p>
<div class="link-box"><a href="{{ ResetLink }}">{{ ResetLink }}</a></div>
<div class="notice">
    <p class="notice-title">Didn''t request this?</p>
    <p class="notice-text">You can safely ignore this email — your password will stay unchanged.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000102', N'ar', N'إعادة تعيين كلمة المرور',
N'<div class="header">
    <p class="eyebrow">أمان الحساب</p>
    <h1>إعادة تعيين كلمة المرور</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">تلقينا طلبًا لإعادة تعيين كلمة المرور الخاصة بحسابك. انقر على الزر أدناه لاختيار كلمة مرور جديدة. هذا الرابط صالح لمدة {{ ExpirationMinutes }} دقيقة.</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">إعادة تعيين كلمة المرور</a>
</div>
<p class="link-fallback">إذا لم يعمل الزر، انسخ الرابط التالي والصقه في متصفحك:</p>
<div class="link-box"><a href="{{ ResetLink }}">{{ ResetLink }}</a></div>
<div class="notice">
    <p class="notice-title">لم تطلب هذا؟</p>
    <p class="notice-text">يمكنك تجاهل هذا البريد الإلكتروني بأمان — ستبقى كلمة المرور الخاصة بك دون تغيير.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000102', N'tr', N'Şifrenizi Sıfırlayın',
N'<div class="header">
    <p class="eyebrow">Hesap güvenliği</p>
    <h1>Şifrenizi sıfırlayın</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">Hesabınızın şifresini sıfırlama talebi aldık. Yeni bir şifre belirlemek için aşağıdaki düğmeye tıklayın. Bu bağlantı {{ ExpirationMinutes }} dakika boyunca geçerlidir.</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">Şifreyi sıfırla</a>
</div>
<p class="link-fallback">Düğme çalışmıyorsa bu bağlantıyı kopyalayıp tarayıcınıza yapıştırın:</p>
<div class="link-box"><a href="{{ ResetLink }}">{{ ResetLink }}</a></div>
<div class="notice">
    <p class="notice-title">Bunu siz talep etmediniz mi?</p>
    <p class="notice-text">Bu e-postayı güvenle yok sayabilirsiniz — şifreniz değişmeden kalacaktır.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000102', N'fr', N'Réinitialisez votre mot de passe',
N'<div class="header">
    <p class="eyebrow">Sécurité du compte</p>
    <h1>Réinitialisez votre mot de passe</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Nous avons reçu une demande de réinitialisation du mot de passe de votre compte. Cliquez sur le bouton ci-dessous pour en choisir un nouveau. Ce lien est valable pendant {{ ExpirationMinutes }} minutes.</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">Réinitialiser le mot de passe</a>
</div>
<p class="link-fallback">Si le bouton ne fonctionne pas, copiez et collez ce lien dans votre navigateur :</p>
<div class="link-box"><a href="{{ ResetLink }}">{{ ResetLink }}</a></div>
<div class="notice">
    <p class="notice-title">Vous n''êtes pas à l''origine de cette demande ?</p>
    <p class="notice-text">Vous pouvez ignorer cet e-mail en toute sécurité — votre mot de passe restera inchangé.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000102', N'zh', N'重置您的密码',
N'<div class="header">
    <p class="eyebrow">账户安全</p>
    <h1>重置您的密码</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">我们收到了重置您账户密码的请求。请点击下方按钮设置新密码。此链接在 {{ ExpirationMinutes }} 分钟内有效。</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">重置密码</a>
</div>
<p class="link-fallback">如果按钮无法使用，请将以下链接复制并粘贴到浏览器中：</p>
<div class="link-box"><a href="{{ ResetLink }}">{{ ResetLink }}</a></div>
<div class="notice">
    <p class="notice-title">不是您本人操作？</p>
    <p class="notice-text">您可以放心忽略此邮件 — 您的密码将保持不变。</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000102', N'ur', N'اپنا پاس ورڈ ری سیٹ کریں',
N'<div class="header">
    <p class="eyebrow">اکاؤنٹ سیکیورٹی</p>
    <h1>اپنا پاس ورڈ ری سیٹ کریں</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">ہمیں آپ کے اکاؤنٹ کا پاس ورڈ ری سیٹ کرنے کی درخواست موصول ہوئی ہے۔ نیا پاس ورڈ منتخب کرنے کے لیے نیچے دیے گئے بٹن پر کلک کریں۔ یہ لنک {{ ExpirationMinutes }} منٹ تک کارآمد ہے۔</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">پاس ورڈ ری سیٹ کریں</a>
</div>
<p class="link-fallback">اگر بٹن کام نہ کرے تو یہ لنک کاپی کر کے اپنے براؤزر میں پیسٹ کریں:</p>
<div class="link-box"><a href="{{ ResetLink }}">{{ ResetLink }}</a></div>
<div class="notice">
    <p class="notice-title">یہ درخواست آپ نے نہیں کی؟</p>
    <p class="notice-text">آپ اس ای میل کو بحفاظت نظر انداز کر سکتے ہیں — آپ کا پاس ورڈ تبدیل نہیں ہوگا۔</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000102', N'fa', N'بازنشانی رمز عبور',
N'<div class="header">
    <p class="eyebrow">امنیت حساب</p>
    <h1>بازنشانی رمز عبور</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">درخواستی برای بازنشانی رمز عبور حساب شما دریافت کردیم. برای انتخاب رمز عبور جدید روی دکمه زیر کلیک کنید. این پیوند تا {{ ExpirationMinutes }} دقیقه معتبر است.</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">بازنشانی رمز عبور</a>
</div>
<p class="link-fallback">اگر دکمه کار نکرد، این پیوند را کپی کرده و در مرورگر خود جای‌گذاری کنید:</p>
<div class="link-box"><a href="{{ ResetLink }}">{{ ResetLink }}</a></div>
<div class="notice">
    <p class="notice-title">این درخواست از شما نبود؟</p>
    <p class="notice-text">می‌توانید با خیال راحت این ایمیل را نادیده بگیرید — رمز عبور شما بدون تغییر باقی می‌ماند.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000102',
        [ModifiedAt] = GETUTCDATE(),
        [ModifiedBy] = @SystemUserId
    WHERE [Id] = '42000000-0000-0000-0000-000000000002';

    PRINT 'password-reset: published redesigned version';
END
ELSE
BEGIN
    PRINT 'password-reset: redesign version already applied (or template missing)';
END
GO

-- ============================================================
-- organization-invitation: new published version in the SEBAKHI-brand design
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000003')
AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateVersions] WHERE [Id] = '43000000-0000-0000-0000-000000000103')
BEGIN
    DECLARE @NextVersion INT =
        (SELECT ISNULL(MAX([VersionNumber]), 0) + 1 FROM [dbo].[NotificationTemplateVersions] WHERE [TemplateId] = '42000000-0000-0000-0000-000000000003');

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000103', '42000000-0000-0000-0000-000000000003', @NextVersion, N'SEBAKHI-brand redesign', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    (NEWID(), '43000000-0000-0000-0000-000000000103', N'en', N'You''re Invited to Join {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">Invitation</p>
    <h1>Join {{ OrganizationName }}</h1>
</div>
<p class="message">Hello,</p>
<p class="message"><strong>{{ InviterName }}</strong> has invited you to join <strong>{{ OrganizationName }}</strong> on {{ Platform.Name }}.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">Accept invitation</a>
</div>
<p class="link-fallback">If the button does not work, copy and paste this link into your browser:</p>
<div class="link-box"><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></div>
<p class="message">Or enter this invitation code on the invitation page:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="muted">This invitation expires on {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC.</p>
<div class="notice">
    <p class="notice-title">Security notice</p>
    <p class="notice-text">If you weren''t expecting this invitation, you can safely ignore this email.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000103', N'ar', N'أنت مدعو للانضمام إلى {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">دعوة</p>
    <h1>انضم إلى {{ OrganizationName }}</h1>
</div>
<p class="message">مرحبًا،</p>
<p class="message">قام <strong>{{ InviterName }}</strong> بدعوتك للانضمام إلى <strong>{{ OrganizationName }}</strong> على {{ Platform.Name }}.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">قبول الدعوة</a>
</div>
<p class="link-fallback">إذا لم يعمل الزر، انسخ الرابط التالي والصقه في متصفحك:</p>
<div class="link-box"><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></div>
<p class="message">أو أدخل رمز الدعوة التالي في صفحة الدعوة:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="muted">تنتهي صلاحية هذه الدعوة في {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC.</p>
<div class="notice">
    <p class="notice-title">تنبيه أمني</p>
    <p class="notice-text">إذا لم تكن تتوقع هذه الدعوة، يمكنك تجاهل هذا البريد الإلكتروني بأمان.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000103', N'tr', N'{{ OrganizationName }} organizasyonuna katılmaya davet edildiniz',
N'<div class="header">
    <p class="eyebrow">Davet</p>
    <h1>{{ OrganizationName }} ekibine katılın</h1>
</div>
<p class="message">Merhaba,</p>
<p class="message"><strong>{{ InviterName }}</strong>, sizi {{ Platform.Name }} üzerinde <strong>{{ OrganizationName }}</strong> ekibine katılmaya davet etti.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">Daveti kabul et</a>
</div>
<p class="link-fallback">Düğme çalışmıyorsa bu bağlantıyı kopyalayıp tarayıcınıza yapıştırın:</p>
<div class="link-box"><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></div>
<p class="message">Veya bu davet kodunu davet sayfasına girin:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="muted">Bu davetin süresi {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC tarihinde dolacaktır.</p>
<div class="notice">
    <p class="notice-title">Güvenlik uyarısı</p>
    <p class="notice-text">Bu daveti beklemiyorduysanız bu e-postayı güvenle yok sayabilirsiniz.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000103', N'fr', N'Vous êtes invité à rejoindre {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">Invitation</p>
    <h1>Rejoignez {{ OrganizationName }}</h1>
</div>
<p class="message">Bonjour,</p>
<p class="message"><strong>{{ InviterName }}</strong> vous a invité à rejoindre <strong>{{ OrganizationName }}</strong> sur {{ Platform.Name }}.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">Accepter l''invitation</a>
</div>
<p class="link-fallback">Si le bouton ne fonctionne pas, copiez et collez ce lien dans votre navigateur :</p>
<div class="link-box"><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></div>
<p class="message">Ou saisissez ce code d''invitation sur la page d''invitation :</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="muted">Cette invitation expire le {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC.</p>
<div class="notice">
    <p class="notice-title">Avis de sécurité</p>
    <p class="notice-text">Si vous n''attendiez pas cette invitation, vous pouvez ignorer cet e-mail en toute sécurité.</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000103', N'zh', N'邀请您加入{{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">邀请</p>
    <h1>加入 {{ OrganizationName }}</h1>
</div>
<p class="message">您好：</p>
<p class="message"><strong>{{ InviterName }}</strong> 邀请您在 {{ Platform.Name }} 上加入 <strong>{{ OrganizationName }}</strong>。</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">接受邀请</a>
</div>
<p class="link-fallback">如果按钮无法使用，请将以下链接复制并粘贴到浏览器中：</p>
<div class="link-box"><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></div>
<p class="message">或在邀请页面输入此邀请码：</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="muted">此邀请将于 {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC 失效。</p>
<div class="notice">
    <p class="notice-title">安全提示</p>
    <p class="notice-text">如果您并未预期收到此邀请，请放心忽略此邮件。</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000103', N'ur', N'آپ کو {{ OrganizationName }} میں شامل ہونے کی دعوت دی گئی ہے',
N'<div class="header">
    <p class="eyebrow">دعوت</p>
    <h1>{{ OrganizationName }} میں شامل ہوں</h1>
</div>
<p class="message">سلام،</p>
<p class="message"><strong>{{ InviterName }}</strong> نے آپ کو {{ Platform.Name }} پر <strong>{{ OrganizationName }}</strong> میں شامل ہونے کی دعوت دی ہے۔</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">دعوت قبول کریں</a>
</div>
<p class="link-fallback">اگر بٹن کام نہ کرے تو یہ لنک کاپی کر کے اپنے براؤزر میں پیسٹ کریں:</p>
<div class="link-box"><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></div>
<p class="message">یا یہ دعوتی کوڈ دعوت کے صفحے پر درج کریں:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="muted">اس دعوت کی میعاد {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC کو ختم ہو جائے گی۔</p>
<div class="notice">
    <p class="notice-title">حفاظتی نوٹس</p>
    <p class="notice-text">اگر آپ کو اس دعوت کی توقع نہیں تھی تو آپ اس ای میل کو بحفاظت نظر انداز کر سکتے ہیں۔</p>
</div>'),
    (NEWID(), '43000000-0000-0000-0000-000000000103', N'fa', N'شما برای پیوستن به {{ OrganizationName }} دعوت شده‌اید',
N'<div class="header">
    <p class="eyebrow">دعوت</p>
    <h1>به {{ OrganizationName }} بپیوندید</h1>
</div>
<p class="message">سلام،</p>
<p class="message"><strong>{{ InviterName }}</strong> شما را به پیوستن به <strong>{{ OrganizationName }}</strong> در {{ Platform.Name }} دعوت کرده است.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">پذیرش دعوت</a>
</div>
<p class="link-fallback">اگر دکمه کار نکرد، این پیوند را کپی کرده و در مرورگر خود جای‌گذاری کنید:</p>
<div class="link-box"><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></div>
<p class="message">یا این کد دعوت را در صفحه دعوت وارد کنید:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="muted">این دعوت‌نامه در {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC منقضی می‌شود.</p>
<div class="notice">
    <p class="notice-title">هشدار امنیتی</p>
    <p class="notice-text">اگر انتظار این دعوت‌نامه را نداشتید، می‌توانید با خیال راحت این ایمیل را نادیده بگیرید.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000103',
        [ModifiedAt] = GETUTCDATE(),
        [ModifiedBy] = @SystemUserId
    WHERE [Id] = '42000000-0000-0000-0000-000000000003';

    PRINT 'organization-invitation: published redesigned version';
END
ELSE
BEGIN
    PRINT 'organization-invitation: redesign version already applied (or template missing)';
END
GO
