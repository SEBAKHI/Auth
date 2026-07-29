-- Notification Templates Seed Data
-- The three system templates in the SEBAKHI-brand design, each with version 1
-- published and all 7 language translations. Class names match the styles defined by
-- the default email layout (11_NotificationLayouts.sql).
-- Guarded per template id so admin-created versions are never clobbered on re-publish.
-- BodyText is left NULL everywhere: the plain-text alternative is derived from BodyHtml.

-- ============================================================
-- Template 1: email-verification (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000001', '42000000-0000-0000-0000-000000000001', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0001-000000000001', '43000000-0000-0000-0000-000000000001', N'en', N'Verify Your Email Address',
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
    ('44000000-0000-0000-0001-000000000002', '43000000-0000-0000-0000-000000000001', N'ar', N'تأكيد عنوان بريدك الإلكتروني',
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
    ('44000000-0000-0000-0001-000000000003', '43000000-0000-0000-0000-000000000001', N'tr', N'E-posta Adresinizi Doğrulayın',
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
    ('44000000-0000-0000-0001-000000000004', '43000000-0000-0000-0000-000000000001', N'fr', N'Vérifiez votre adresse e-mail',
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
    ('44000000-0000-0000-0001-000000000005', '43000000-0000-0000-0000-000000000001', N'zh', N'验证您的邮箱地址',
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
    ('44000000-0000-0000-0001-000000000006', '43000000-0000-0000-0000-000000000001', N'ur', N'اپنا ای میل ایڈریس تصدیق کریں',
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
    ('44000000-0000-0000-0001-000000000007', '43000000-0000-0000-0000-000000000001', N'fa', N'تأیید آدرس ایمیل شما',
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
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000001'
    WHERE [Id] = '42000000-0000-0000-0000-000000000001';

    PRINT 'Created email-verification template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'email-verification template already exists';
END
GO

-- ============================================================
-- Template 2: password-reset (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000002', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000002', '42000000-0000-0000-0000-000000000002', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0002-000000000001', '43000000-0000-0000-0000-000000000002', N'en', N'Reset Your Password',
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
    ('44000000-0000-0000-0002-000000000002', '43000000-0000-0000-0000-000000000002', N'ar', N'إعادة تعيين كلمة المرور',
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
    ('44000000-0000-0000-0002-000000000003', '43000000-0000-0000-0000-000000000002', N'tr', N'Şifrenizi Sıfırlayın',
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
    ('44000000-0000-0000-0002-000000000004', '43000000-0000-0000-0000-000000000002', N'fr', N'Réinitialisez votre mot de passe',
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
    ('44000000-0000-0000-0002-000000000005', '43000000-0000-0000-0000-000000000002', N'zh', N'重置您的密码',
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
    ('44000000-0000-0000-0002-000000000006', '43000000-0000-0000-0000-000000000002', N'ur', N'اپنا پاس ورڈ ری سیٹ کریں',
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
    ('44000000-0000-0000-0002-000000000007', '43000000-0000-0000-0000-000000000002', N'fa', N'بازنشانی رمز عبور',
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
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000002'
    WHERE [Id] = '42000000-0000-0000-0000-000000000002';

    PRINT 'Created password-reset template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'password-reset template already exists';
END
GO

-- ============================================================
-- Template 3: organization-invitation (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000003')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000003', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000003', '42000000-0000-0000-0000-000000000003', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0003-000000000001', '43000000-0000-0000-0000-000000000003', N'en', N'You''re Invited to Join {{ OrganizationName }}',
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
    ('44000000-0000-0000-0003-000000000002', '43000000-0000-0000-0000-000000000003', N'ar', N'أنت مدعو للانضمام إلى {{ OrganizationName }}',
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
    ('44000000-0000-0000-0003-000000000003', '43000000-0000-0000-0000-000000000003', N'tr', N'{{ OrganizationName }} organizasyonuna katılmaya davet edildiniz',
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
    ('44000000-0000-0000-0003-000000000004', '43000000-0000-0000-0000-000000000003', N'fr', N'Vous êtes invité à rejoindre {{ OrganizationName }}',
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
    ('44000000-0000-0000-0003-000000000005', '43000000-0000-0000-0000-000000000003', N'zh', N'邀请您加入{{ OrganizationName }}',
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
    ('44000000-0000-0000-0003-000000000006', '43000000-0000-0000-0000-000000000003', N'ur', N'آپ کو {{ OrganizationName }} میں شامل ہونے کی دعوت دی گئی ہے',
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
    ('44000000-0000-0000-0003-000000000007', '43000000-0000-0000-0000-000000000003', N'fa', N'شما برای پیوستن به {{ OrganizationName }} دعوت شده‌اید',
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
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000003'
    WHERE [Id] = '42000000-0000-0000-0000-000000000003';

    PRINT 'Created organization-invitation template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'organization-invitation template already exists';
END
GO

-- ============================================================
-- Template 4: ownership-transfer-code (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000005', '40000000-0000-0000-0000-000000000005', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000005', '42000000-0000-0000-0000-000000000005', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0005-000000000001', '43000000-0000-0000-0000-000000000005', N'en', N'Ownership transfer code for {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">Ownership transfer</p>
    <h1>Confirm the ownership transfer</h1>
</div>
<p class="message">Hello {{ TargetName }},</p>
<p class="message"><strong>{{ OwnerName }}</strong> wants to transfer ownership of the organization <strong>{{ OrganizationName }}</strong> to you. Share the code below with {{ OwnerName }} to approve the transfer — entering it completes the handover. It expires in {{ ExpirationMinutes }} minutes.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Security notice</p>
    <p class="notice-text">If you do not want to become the owner of this organization, ignore this email and do not share the code with anyone. {{ Platform.Name }} will never ask you for it.</p>
</div>'),
    ('44000000-0000-0000-0005-000000000002', '43000000-0000-0000-0000-000000000005', N'ar', N'رمز تأكيد نقل ملكية {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">نقل الملكية</p>
    <h1>تأكيد نقل الملكية</h1>
</div>
<p class="message">مرحبًا {{ TargetName }}،</p>
<p class="message">يرغب <strong>{{ OwnerName }}</strong> في نقل ملكية منظمة <strong>{{ OrganizationName }}</strong> إليك. شارك الرمز أدناه مع {{ OwnerName }} للموافقة على النقل — إدخاله يُتمّ عملية التسليم. تنتهي صلاحيته خلال {{ ExpirationMinutes }} دقيقة.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">تنبيه أمني</p>
    <p class="notice-text">إذا كنت لا ترغب في أن تصبح مالك هذه المنظمة، فتجاهل هذا البريد ولا تشارك الرمز مع أي أحد. لن يطلبه منك فريق {{ Platform.Name }} أبدًا.</p>
</div>'),
    ('44000000-0000-0000-0005-000000000003', '43000000-0000-0000-0000-000000000005', N'tr', N'{{ OrganizationName }} sahiplik devri onay kodu',
N'<div class="header">
    <p class="eyebrow">Sahiplik devri</p>
    <h1>Sahiplik devrini onaylayın</h1>
</div>
<p class="message">Merhaba {{ TargetName }},</p>
<p class="message"><strong>{{ OwnerName }}</strong>, <strong>{{ OrganizationName }}</strong> organizasyonunun sahipliğini size devretmek istiyor. Devri onaylamak için aşağıdaki kodu {{ OwnerName }} ile paylaşın — kodun girilmesi devri tamamlar. Kodun süresi {{ ExpirationMinutes }} dakika içinde dolacaktır.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Güvenlik uyarısı</p>
    <p class="notice-text">Bu organizasyonun sahibi olmak istemiyorsanız bu e-postayı yok sayın ve kodu kimseyle paylaşmayın. {{ Platform.Name }} bu kodu sizden hiçbir zaman istemez.</p>
</div>'),
    ('44000000-0000-0000-0005-000000000004', '43000000-0000-0000-0000-000000000005', N'fr', N'Code de confirmation du transfert de propriété de {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">Transfert de propriété</p>
    <h1>Confirmez le transfert de propriété</h1>
</div>
<p class="message">Bonjour {{ TargetName }},</p>
<p class="message"><strong>{{ OwnerName }}</strong> souhaite vous transférer la propriété de l''organisation <strong>{{ OrganizationName }}</strong>. Partagez le code ci-dessous avec {{ OwnerName }} pour approuver le transfert — sa saisie finalise la passation. Il expirera dans {{ ExpirationMinutes }} minutes.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Avis de sécurité</p>
    <p class="notice-text">Si vous ne souhaitez pas devenir propriétaire de cette organisation, ignorez cet e-mail et ne partagez le code avec personne. {{ Platform.Name }} ne vous le demandera jamais.</p>
</div>'),
    ('44000000-0000-0000-0005-000000000005', '43000000-0000-0000-0000-000000000005', N'zh', N'{{ OrganizationName }} 所有权转移确认码',
N'<div class="header">
    <p class="eyebrow">所有权转移</p>
    <h1>确认所有权转移</h1>
</div>
<p class="message">您好 {{ TargetName }}，</p>
<p class="message"><strong>{{ OwnerName }}</strong> 希望将组织 <strong>{{ OrganizationName }}</strong> 的所有权转移给您。请将以下确认码告知 {{ OwnerName }} 以批准此次转移 — 输入该码即可完成交接。确认码将在 {{ ExpirationMinutes }} 分钟后失效。</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">安全提示</p>
    <p class="notice-text">如果您不想成为该组织的所有者，请忽略此邮件，并且不要向任何人透露此码。{{ Platform.Name }} 绝不会向您索取。</p>
</div>'),
    ('44000000-0000-0000-0005-000000000006', '43000000-0000-0000-0000-000000000005', N'ur', N'{{ OrganizationName }} کی ملکیت کی منتقلی کا تصدیقی کوڈ',
N'<div class="header">
    <p class="eyebrow">ملکیت کی منتقلی</p>
    <h1>ملکیت کی منتقلی کی تصدیق کریں</h1>
</div>
<p class="message">السلام علیکم {{ TargetName }}،</p>
<p class="message"><strong>{{ OwnerName }}</strong> تنظیم <strong>{{ OrganizationName }}</strong> کی ملکیت آپ کو منتقل کرنا چاہتے ہیں۔ منتقلی کی منظوری کے لیے نیچے دیا گیا کوڈ {{ OwnerName }} کے ساتھ شیئر کریں — کوڈ درج ہونے پر منتقلی مکمل ہو جائے گی۔ اس کی میعاد {{ ExpirationMinutes }} منٹ میں ختم ہو جائے گی۔</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">حفاظتی نوٹس</p>
    <p class="notice-text">اگر آپ اس تنظیم کے مالک نہیں بننا چاہتے تو اس ای میل کو نظر انداز کریں اور کوڈ کسی کے ساتھ شیئر نہ کریں۔ {{ Platform.Name }} کبھی آپ سے یہ کوڈ نہیں مانگے گا۔</p>
</div>'),
    ('44000000-0000-0000-0005-000000000007', '43000000-0000-0000-0000-000000000005', N'fa', N'کد تأیید انتقال مالکیت {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">انتقال مالکیت</p>
    <h1>انتقال مالکیت را تأیید کنید</h1>
</div>
<p class="message">سلام {{ TargetName }}،</p>
<p class="message"><strong>{{ OwnerName }}</strong> می‌خواهد مالکیت سازمان <strong>{{ OrganizationName }}</strong> را به شما منتقل کند. برای تأیید انتقال، کد زیر را با {{ OwnerName }} در میان بگذارید — با وارد شدن کد، انتقال کامل می‌شود. این کد تا {{ ExpirationMinutes }} دقیقه دیگر منقضی می‌شود.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">هشدار امنیتی</p>
    <p class="notice-text">اگر نمی‌خواهید مالک این سازمان شوید، این ایمیل را نادیده بگیرید و کد را با کسی به اشتراک نگذارید. {{ Platform.Name }} هرگز آن را از شما نمی‌خواهد.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000005'
    WHERE [Id] = '42000000-0000-0000-0000-000000000005';

    PRINT 'Created ownership-transfer-code template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'ownership-transfer-code template already exists';
END
GO

-- ============================================================
-- Template 5: ownership-transferred (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000006')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000006', '40000000-0000-0000-0000-000000000006', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000006', '42000000-0000-0000-0000-000000000006', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0006-000000000001', '43000000-0000-0000-0000-000000000006', N'en', N'Ownership of {{ OrganizationName }} has been transferred',
N'<div class="header">
    <p class="eyebrow">Ownership transfer</p>
    <h1>Ownership has been transferred</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">Ownership of the organization <strong>{{ OrganizationName }}</strong> has been transferred from <strong>{{ PreviousOwnerName }}</strong> to <strong>{{ NewOwnerName }}</strong>. The previous owner''s membership now has administrator access.</p>
<div class="notice">
    <p class="notice-title">Security notice</p>
    <p class="notice-text">If you did not expect this change, contact your administrator or {{ Platform.Name }} support immediately.</p>
</div>'),
    ('44000000-0000-0000-0006-000000000002', '43000000-0000-0000-0000-000000000006', N'ar', N'تم نقل ملكية {{ OrganizationName }}',
N'<div class="header">
    <p class="eyebrow">نقل الملكية</p>
    <h1>تم نقل الملكية</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">تم نقل ملكية منظمة <strong>{{ OrganizationName }}</strong> من <strong>{{ PreviousOwnerName }}</strong> إلى <strong>{{ NewOwnerName }}</strong>. أصبحت عضوية المالك السابق الآن بصلاحيات مشرف.</p>
<div class="notice">
    <p class="notice-title">تنبيه أمني</p>
    <p class="notice-text">إذا لم تكن تتوقع هذا التغيير، فتواصل فورًا مع مشرفك أو مع دعم {{ Platform.Name }}.</p>
</div>'),
    ('44000000-0000-0000-0006-000000000003', '43000000-0000-0000-0000-000000000006', N'tr', N'{{ OrganizationName }} sahipliği devredildi',
N'<div class="header">
    <p class="eyebrow">Sahiplik devri</p>
    <h1>Sahiplik devredildi</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message"><strong>{{ OrganizationName }}</strong> organizasyonunun sahipliği <strong>{{ PreviousOwnerName }}</strong> adlı kullanıcıdan <strong>{{ NewOwnerName }}</strong> adlı kullanıcıya devredildi. Önceki sahibin üyeliği artık yönetici erişimine sahiptir.</p>
<div class="notice">
    <p class="notice-title">Güvenlik uyarısı</p>
    <p class="notice-text">Bu değişikliği beklemiyorsanız derhal yöneticinizle veya {{ Platform.Name }} destek ekibiyle iletişime geçin.</p>
</div>'),
    ('44000000-0000-0000-0006-000000000004', '43000000-0000-0000-0000-000000000006', N'fr', N'La propriété de {{ OrganizationName }} a été transférée',
N'<div class="header">
    <p class="eyebrow">Transfert de propriété</p>
    <h1>La propriété a été transférée</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">La propriété de l''organisation <strong>{{ OrganizationName }}</strong> a été transférée de <strong>{{ PreviousOwnerName }}</strong> à <strong>{{ NewOwnerName }}</strong>. L''ancien propriétaire dispose désormais d''un accès administrateur.</p>
<div class="notice">
    <p class="notice-title">Avis de sécurité</p>
    <p class="notice-text">Si vous n''attendiez pas ce changement, contactez immédiatement votre administrateur ou l''assistance {{ Platform.Name }}.</p>
</div>'),
    ('44000000-0000-0000-0006-000000000005', '43000000-0000-0000-0000-000000000006', N'zh', N'{{ OrganizationName }} 的所有权已转移',
N'<div class="header">
    <p class="eyebrow">所有权转移</p>
    <h1>所有权已转移</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">组织 <strong>{{ OrganizationName }}</strong> 的所有权已从 <strong>{{ PreviousOwnerName }}</strong> 转移给 <strong>{{ NewOwnerName }}</strong>。前所有者的成员身份现为管理员权限。</p>
<div class="notice">
    <p class="notice-title">安全提示</p>
    <p class="notice-text">如果您并未预期此变更，请立即联系您的管理员或 {{ Platform.Name }} 支持团队。</p>
</div>'),
    ('44000000-0000-0000-0006-000000000006', '43000000-0000-0000-0000-000000000006', N'ur', N'{{ OrganizationName }} کی ملکیت منتقل کر دی گئی',
N'<div class="header">
    <p class="eyebrow">ملکیت کی منتقلی</p>
    <h1>ملکیت منتقل کر دی گئی</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">تنظیم <strong>{{ OrganizationName }}</strong> کی ملکیت <strong>{{ PreviousOwnerName }}</strong> سے <strong>{{ NewOwnerName }}</strong> کو منتقل کر دی گئی ہے۔ سابقہ مالک کی رکنیت اب منتظم کی رسائی رکھتی ہے۔</p>
<div class="notice">
    <p class="notice-title">حفاظتی نوٹس</p>
    <p class="notice-text">اگر آپ کو اس تبدیلی کی توقع نہیں تھی تو فوری طور پر اپنے منتظم یا {{ Platform.Name }} سپورٹ سے رابطہ کریں۔</p>
</div>'),
    ('44000000-0000-0000-0006-000000000007', '43000000-0000-0000-0000-000000000006', N'fa', N'مالکیت {{ OrganizationName }} منتقل شد',
N'<div class="header">
    <p class="eyebrow">انتقال مالکیت</p>
    <h1>مالکیت منتقل شد</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">مالکیت سازمان <strong>{{ OrganizationName }}</strong> از <strong>{{ PreviousOwnerName }}</strong> به <strong>{{ NewOwnerName }}</strong> منتقل شد. عضویت مالک قبلی اکنون دسترسی مدیر دارد.</p>
<div class="notice">
    <p class="notice-title">هشدار امنیتی</p>
    <p class="notice-text">اگر انتظار این تغییر را نداشتید، بلافاصله با مدیر خود یا پشتیبانی {{ Platform.Name }} تماس بگیرید.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000006'
    WHERE [Id] = '42000000-0000-0000-0000-000000000006';

    PRINT 'Created ownership-transferred template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'ownership-transferred template already exists';
END
GO

-- ============================================================
-- Template 7: account-deletion-requested (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000007')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000007', '40000000-0000-0000-0000-000000000007', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000007', '42000000-0000-0000-0000-000000000007', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0007-000000000001', '43000000-0000-0000-0000-000000000007', N'en', N'Your Account Deletion Has Been Scheduled',
N'<div class="header">
    <p class="eyebrow">Account deletion</p>
    <h1>Your account has been deactivated</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">We received a request to delete your account. It has been deactivated and signed out everywhere, and will be permanently deleted on <strong>{{ GraceEndsAt }}</strong> — {{ GraceDays }} days from now.</p>
<p class="message">Changed your mind? You can restore your account any time before that date.</p>
<div class="button-container">
    <a class="button" href="{{ RecoveryLink }}">Restore my account</a>
</div>
<p class="link-fallback">If the button does not work, copy and paste this link into your browser:</p>
<div class="link-box"><a href="{{ RecoveryLink }}">{{ RecoveryLink }}</a></div>
<div class="notice">
    <p class="notice-title">Security notice</p>
    <p class="notice-text">If you did not request this deletion, someone else may have access to your credentials. Restore your account now and change your password immediately.</p>
</div>'),
    ('44000000-0000-0000-0007-000000000002', '43000000-0000-0000-0000-000000000007', N'ar', N'تمت جدولة حذف حسابك',
N'<div class="header">
    <p class="eyebrow">حذف الحساب</p>
    <h1>تم تعطيل حسابك</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">لقد تلقينا طلبًا لحذف حسابك. تم تعطيل الحساب وتسجيل الخروج من جميع الأجهزة، وسيتم حذفه نهائيًا في <strong>{{ GraceEndsAt }}</strong> — أي بعد {{ GraceDays }} يومًا من الآن.</p>
<p class="message">هل غيرت رأيك؟ يمكنك استعادة حسابك في أي وقت قبل ذلك التاريخ.</p>
<div class="button-container">
    <a class="button" href="{{ RecoveryLink }}">استعادة حسابي</a>
</div>
<p class="link-fallback">إذا لم يعمل الزر، انسخ الرابط التالي والصقه في متصفحك:</p>
<div class="link-box"><a href="{{ RecoveryLink }}">{{ RecoveryLink }}</a></div>
<div class="notice">
    <p class="notice-title">تنبيه أمني</p>
    <p class="notice-text">إذا لم تطلب هذا الحذف، فقد يكون شخص آخر قد وصل إلى بيانات اعتمادك. استعد حسابك الآن وغيّر كلمة مرورك فورًا.</p>
</div>'),
    ('44000000-0000-0000-0007-000000000003', '43000000-0000-0000-0000-000000000007', N'tr', N'Hesabınızın Silinmesi Planlandı',
N'<div class="header">
    <p class="eyebrow">Hesap silme</p>
    <h1>Hesabınız devre dışı bırakıldı</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">Hesabınızın silinmesi için bir talep aldık. Hesabınız devre dışı bırakıldı ve tüm cihazlardan çıkış yapıldı; <strong>{{ GraceEndsAt }}</strong> tarihinde — bugünden itibaren {{ GraceDays }} gün sonra — kalıcı olarak silinecektir.</p>
<p class="message">Fikriniz mi değişti? Bu tarihten önce hesabınızı istediğiniz zaman geri yükleyebilirsiniz.</p>
<div class="button-container">
    <a class="button" href="{{ RecoveryLink }}">Hesabımı geri yükle</a>
</div>
<p class="link-fallback">Düğme çalışmazsa şu bağlantıyı kopyalayıp tarayıcınıza yapıştırın:</p>
<div class="link-box"><a href="{{ RecoveryLink }}">{{ RecoveryLink }}</a></div>
<div class="notice">
    <p class="notice-title">Güvenlik uyarısı</p>
    <p class="notice-text">Bu silme talebini siz göndermediyseniz kimlik bilgilerinize başka biri erişmiş olabilir. Hesabınızı hemen geri yükleyin ve parolanızı derhal değiştirin.</p>
</div>'),
    ('44000000-0000-0000-0007-000000000004', '43000000-0000-0000-0000-000000000007', N'fr', N'La suppression de votre compte a été programmée',
N'<div class="header">
    <p class="eyebrow">Suppression du compte</p>
    <h1>Votre compte a été désactivé</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Nous avons reçu une demande de suppression de votre compte. Il a été désactivé et déconnecté de tous les appareils, et sera définitivement supprimé le <strong>{{ GraceEndsAt }}</strong>, soit dans {{ GraceDays }} jours.</p>
<p class="message">Vous avez changé d''avis ? Vous pouvez restaurer votre compte à tout moment avant cette date.</p>
<div class="button-container">
    <a class="button" href="{{ RecoveryLink }}">Restaurer mon compte</a>
</div>
<p class="link-fallback">Si le bouton ne fonctionne pas, copiez et collez ce lien dans votre navigateur :</p>
<div class="link-box"><a href="{{ RecoveryLink }}">{{ RecoveryLink }}</a></div>
<div class="notice">
    <p class="notice-title">Avis de sécurité</p>
    <p class="notice-text">Si vous n''avez pas demandé cette suppression, quelqu''un d''autre a peut-être accès à vos identifiants. Restaurez votre compte maintenant et changez immédiatement votre mot de passe.</p>
</div>'),
    ('44000000-0000-0000-0007-000000000005', '43000000-0000-0000-0000-000000000007', N'zh', N'您的账户删除已安排',
N'<div class="header">
    <p class="eyebrow">账户删除</p>
    <h1>您的账户已停用</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">我们收到了删除您账户的请求。账户已停用并在所有设备上退出登录，将于 <strong>{{ GraceEndsAt }}</strong>（{{ GraceDays }} 天后）被永久删除。</p>
<p class="message">改变主意了？在该日期之前，您可以随时恢复账户。</p>
<div class="button-container">
    <a class="button" href="{{ RecoveryLink }}">恢复我的账户</a>
</div>
<p class="link-fallback">如果按钮无法使用，请复制以下链接并粘贴到浏览器中：</p>
<div class="link-box"><a href="{{ RecoveryLink }}">{{ RecoveryLink }}</a></div>
<div class="notice">
    <p class="notice-title">安全提示</p>
    <p class="notice-text">如果这不是您本人发起的删除请求，您的凭据可能已被他人获取。请立即恢复账户并修改密码。</p>
</div>'),
    ('44000000-0000-0000-0007-000000000006', '43000000-0000-0000-0000-000000000007', N'ur', N'آپ کے اکاؤنٹ کو حذف کرنے کا عمل طے کر دیا گیا ہے',
N'<div class="header">
    <p class="eyebrow">اکاؤنٹ کا حذف</p>
    <h1>آپ کا اکاؤنٹ غیر فعال کر دیا گیا ہے</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">ہمیں آپ کا اکاؤنٹ حذف کرنے کی درخواست موصول ہوئی ہے۔ اکاؤنٹ غیر فعال کر دیا گیا ہے اور تمام آلات سے سائن آؤٹ کر دیا گیا ہے، اور یہ <strong>{{ GraceEndsAt }}</strong> کو — آج سے {{ GraceDays }} دن بعد — مستقل طور پر حذف کر دیا جائے گا۔</p>
<p class="message">ارادہ بدل گیا؟ آپ اس تاریخ سے پہلے کسی بھی وقت اپنا اکاؤنٹ بحال کر سکتے ہیں۔</p>
<div class="button-container">
    <a class="button" href="{{ RecoveryLink }}">میرا اکاؤنٹ بحال کریں</a>
</div>
<p class="link-fallback">اگر بٹن کام نہ کرے تو یہ لنک کاپی کر کے اپنے براؤزر میں پیسٹ کریں:</p>
<div class="link-box"><a href="{{ RecoveryLink }}">{{ RecoveryLink }}</a></div>
<div class="notice">
    <p class="notice-title">حفاظتی نوٹس</p>
    <p class="notice-text">اگر آپ نے یہ درخواست نہیں دی تو ممکن ہے کسی اور کو آپ کی لاگ اِن معلومات تک رسائی حاصل ہو۔ فوراً اپنا اکاؤنٹ بحال کریں اور اپنا پاس ورڈ تبدیل کریں۔</p>
</div>'),
    ('44000000-0000-0000-0007-000000000007', '43000000-0000-0000-0000-000000000007', N'fa', N'حذف حساب شما زمان‌بندی شد',
N'<div class="header">
    <p class="eyebrow">حذف حساب</p>
    <h1>حساب شما غیرفعال شد</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">درخواستی برای حذف حساب شما دریافت کردیم. حساب غیرفعال و از همه دستگاه‌ها خارج شده است و در تاریخ <strong>{{ GraceEndsAt }}</strong> — یعنی {{ GraceDays }} روز دیگر — برای همیشه حذف خواهد شد.</p>
<p class="message">نظرتان عوض شد؟ تا پیش از آن تاریخ می‌توانید هر زمان حساب خود را بازیابی کنید.</p>
<div class="button-container">
    <a class="button" href="{{ RecoveryLink }}">بازیابی حساب من</a>
</div>
<p class="link-fallback">اگر دکمه کار نکرد، این پیوند را کپی و در مرورگر خود وارد کنید:</p>
<div class="link-box"><a href="{{ RecoveryLink }}">{{ RecoveryLink }}</a></div>
<div class="notice">
    <p class="notice-title">هشدار امنیتی</p>
    <p class="notice-text">اگر شما این درخواست حذف را نداده‌اید، ممکن است شخص دیگری به اطلاعات ورود شما دسترسی داشته باشد. همین حالا حساب خود را بازیابی کنید و بلافاصله رمز عبور خود را تغییر دهید.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000007'
    WHERE [Id] = '42000000-0000-0000-0000-000000000007';

    PRINT 'Created account-deletion-requested template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'account-deletion-requested template already exists';
END
GO

-- ============================================================
-- Template 8: account-deletion-verification (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000008')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000008', '40000000-0000-0000-0000-000000000008', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000008', '42000000-0000-0000-0000-000000000008', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0008-000000000001', '43000000-0000-0000-0000-000000000008', N'en', N'Confirm Your Account Deletion',
N'<div class="header">
    <p class="eyebrow">Account deletion</p>
    <h1>Confirm your deletion request</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">Enter the code below to confirm the deletion of your account. It expires in {{ ExpirationMinutes }} minutes.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Security notice</p>
    <p class="notice-text">If you did not request account deletion, do not enter this code anywhere and consider changing your password. Never share this code with anyone — {{ Platform.Name }} will never ask you for it.</p>
</div>'),
    ('44000000-0000-0000-0008-000000000002', '43000000-0000-0000-0000-000000000008', N'ar', N'تأكيد حذف حسابك',
N'<div class="header">
    <p class="eyebrow">حذف الحساب</p>
    <h1>تأكيد طلب الحذف</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">أدخل الرمز أدناه لتأكيد حذف حسابك. تنتهي صلاحيته خلال {{ ExpirationMinutes }} دقيقة.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">تنبيه أمني</p>
    <p class="notice-text">إذا لم تطلب حذف الحساب، فلا تُدخل هذا الرمز في أي مكان وفكّر في تغيير كلمة مرورك. لا تشارك هذا الرمز مع أي شخص — لن يطلبه منك فريق {{ Platform.Name }} أبدًا.</p>
</div>'),
    ('44000000-0000-0000-0008-000000000003', '43000000-0000-0000-0000-000000000008', N'tr', N'Hesap Silme İşlemini Onaylayın',
N'<div class="header">
    <p class="eyebrow">Hesap silme</p>
    <h1>Silme talebinizi onaylayın</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">Hesabınızın silinmesini onaylamak için aşağıdaki kodu girin. Kodun süresi {{ ExpirationMinutes }} dakika içinde dolacaktır.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Güvenlik uyarısı</p>
    <p class="notice-text">Hesap silme talebinde bulunmadıysanız bu kodu hiçbir yere girmeyin ve parolanızı değiştirmeyi düşünün. Bu kodu asla kimseyle paylaşmayın — {{ Platform.Name }} bu kodu sizden hiçbir zaman istemez.</p>
</div>'),
    ('44000000-0000-0000-0008-000000000004', '43000000-0000-0000-0000-000000000008', N'fr', N'Confirmez la suppression de votre compte',
N'<div class="header">
    <p class="eyebrow">Suppression du compte</p>
    <h1>Confirmez votre demande de suppression</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Saisissez le code ci-dessous pour confirmer la suppression de votre compte. Il expirera dans {{ ExpirationMinutes }} minutes.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">Avis de sécurité</p>
    <p class="notice-text">Si vous n''avez pas demandé la suppression de votre compte, ne saisissez ce code nulle part et envisagez de changer votre mot de passe. Ne partagez jamais ce code — {{ Platform.Name }} ne vous le demandera jamais.</p>
</div>'),
    ('44000000-0000-0000-0008-000000000005', '43000000-0000-0000-0000-000000000008', N'zh', N'确认删除您的账户',
N'<div class="header">
    <p class="eyebrow">账户删除</p>
    <h1>确认您的删除请求</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">请输入以下验证码以确认删除您的账户。验证码将在 {{ ExpirationMinutes }} 分钟后失效。</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">安全提示</p>
    <p class="notice-text">如果您并未请求删除账户，请勿在任何地方输入此验证码，并考虑修改密码。请勿与任何人分享此验证码 — {{ Platform.Name }} 绝不会向您索取。</p>
</div>'),
    ('44000000-0000-0000-0008-000000000006', '43000000-0000-0000-0000-000000000008', N'ur', N'اپنے اکاؤنٹ کے حذف کی تصدیق کریں',
N'<div class="header">
    <p class="eyebrow">اکاؤنٹ کا حذف</p>
    <h1>اپنی حذف کرنے کی درخواست کی تصدیق کریں</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">اپنے اکاؤنٹ کے حذف کی تصدیق کے لیے نیچے دیا گیا کوڈ درج کریں۔ اس کی میعاد {{ ExpirationMinutes }} منٹ میں ختم ہو جائے گی۔</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">حفاظتی نوٹس</p>
    <p class="notice-text">اگر آپ نے اکاؤنٹ حذف کرنے کی درخواست نہیں دی تو یہ کوڈ کہیں بھی درج نہ کریں اور اپنا پاس ورڈ تبدیل کرنے پر غور کریں۔ یہ کوڈ کبھی کسی کے ساتھ شیئر نہ کریں — {{ Platform.Name }} کبھی آپ سے یہ کوڈ نہیں مانگے گا۔</p>
</div>'),
    ('44000000-0000-0000-0008-000000000007', '43000000-0000-0000-0000-000000000008', N'fa', N'حذف حساب خود را تأیید کنید',
N'<div class="header">
    <p class="eyebrow">حذف حساب</p>
    <h1>درخواست حذف خود را تأیید کنید</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">برای تأیید حذف حساب خود، کد زیر را وارد کنید. این کد تا {{ ExpirationMinutes }} دقیقه دیگر منقضی می‌شود.</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<div class="notice">
    <p class="notice-title">هشدار امنیتی</p>
    <p class="notice-text">اگر شما حذف حساب را درخواست نکرده‌اید، این کد را در هیچ جایی وارد نکنید و تغییر رمز عبور خود را در نظر بگیرید. این کد را هرگز با کسی به اشتراک نگذارید — {{ Platform.Name }} هرگز آن را از شما نمی‌خواهد.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000008'
    WHERE [Id] = '42000000-0000-0000-0000-000000000008';

    PRINT 'Created account-deletion-verification template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'account-deletion-verification template already exists';
END
GO

-- ============================================================
-- Template 9: account-deletion-cancelled (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000009')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000009', '40000000-0000-0000-0000-000000000009', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000009', '42000000-0000-0000-0000-000000000009', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0009-000000000001', '43000000-0000-0000-0000-000000000009', N'en', N'Your Account Has Been Restored',
N'<div class="header">
    <p class="eyebrow">Account deletion</p>
    <h1>Your account is back</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">The pending deletion of your account was cancelled on <strong>{{ CancelledAt }}</strong> and your account has been fully restored. Welcome back!</p>
<div class="notice">
    <p class="notice-title">Security notice</p>
    <p class="notice-text">If you did not restore this account yourself, someone else may know your credentials — change your password immediately.</p>
</div>'),
    ('44000000-0000-0000-0009-000000000002', '43000000-0000-0000-0000-000000000009', N'ar', N'تمت استعادة حسابك',
N'<div class="header">
    <p class="eyebrow">حذف الحساب</p>
    <h1>عاد حسابك</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">تم إلغاء الحذف المعلق لحسابك في <strong>{{ CancelledAt }}</strong> وتمت استعادة حسابك بالكامل. مرحبًا بعودتك!</p>
<div class="notice">
    <p class="notice-title">تنبيه أمني</p>
    <p class="notice-text">إذا لم تكن أنت من استعاد هذا الحساب، فقد يعرف شخص آخر بيانات اعتمادك — غيّر كلمة مرورك فورًا.</p>
</div>'),
    ('44000000-0000-0000-0009-000000000003', '43000000-0000-0000-0000-000000000009', N'tr', N'Hesabınız Geri Yüklendi',
N'<div class="header">
    <p class="eyebrow">Hesap silme</p>
    <h1>Hesabınız geri döndü</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">Hesabınızın bekleyen silme işlemi <strong>{{ CancelledAt }}</strong> tarihinde iptal edildi ve hesabınız tamamen geri yüklendi. Tekrar hoş geldiniz!</p>
<div class="notice">
    <p class="notice-title">Güvenlik uyarısı</p>
    <p class="notice-text">Bu hesabı siz geri yüklemediyseniz kimlik bilgilerinizi başka biri biliyor olabilir — parolanızı derhal değiştirin.</p>
</div>'),
    ('44000000-0000-0000-0009-000000000004', '43000000-0000-0000-0000-000000000009', N'fr', N'Votre compte a été restauré',
N'<div class="header">
    <p class="eyebrow">Suppression du compte</p>
    <h1>Votre compte est de retour</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">La suppression en attente de votre compte a été annulée le <strong>{{ CancelledAt }}</strong> et votre compte a été entièrement restauré. Bon retour parmi nous !</p>
<div class="notice">
    <p class="notice-title">Avis de sécurité</p>
    <p class="notice-text">Si vous n''avez pas restauré ce compte vous-même, quelqu''un d''autre connaît peut-être vos identifiants — changez immédiatement votre mot de passe.</p>
</div>'),
    ('44000000-0000-0000-0009-000000000005', '43000000-0000-0000-0000-000000000009', N'zh', N'您的账户已恢复',
N'<div class="header">
    <p class="eyebrow">账户删除</p>
    <h1>您的账户已恢复</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">您账户的待删除请求已于 <strong>{{ CancelledAt }}</strong> 取消，账户已完全恢复。欢迎回来！</p>
<div class="notice">
    <p class="notice-title">安全提示</p>
    <p class="notice-text">如果不是您本人恢复了该账户，您的凭据可能已被他人知晓 — 请立即修改密码。</p>
</div>'),
    ('44000000-0000-0000-0009-000000000006', '43000000-0000-0000-0000-000000000009', N'ur', N'آپ کا اکاؤنٹ بحال کر دیا گیا ہے',
N'<div class="header">
    <p class="eyebrow">اکاؤنٹ کا حذف</p>
    <h1>آپ کا اکاؤنٹ واپس آ گیا ہے</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">آپ کے اکاؤنٹ کا زیر التوا حذف <strong>{{ CancelledAt }}</strong> کو منسوخ کر دیا گیا اور آپ کا اکاؤنٹ مکمل طور پر بحال کر دیا گیا ہے۔ خوش آمدید!</p>
<div class="notice">
    <p class="notice-title">حفاظتی نوٹس</p>
    <p class="notice-text">اگر آپ نے خود یہ اکاؤنٹ بحال نہیں کیا تو ممکن ہے کسی اور کو آپ کی لاگ اِن معلومات معلوم ہوں — فوراً اپنا پاس ورڈ تبدیل کریں۔</p>
</div>'),
    ('44000000-0000-0000-0009-000000000007', '43000000-0000-0000-0000-000000000009', N'fa', N'حساب شما بازیابی شد',
N'<div class="header">
    <p class="eyebrow">حذف حساب</p>
    <h1>حساب شما برگشت</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">حذف در انتظارِ حساب شما در تاریخ <strong>{{ CancelledAt }}</strong> لغو شد و حساب شما به‌طور کامل بازیابی شده است. خوش برگشتید!</p>
<div class="notice">
    <p class="notice-title">هشدار امنیتی</p>
    <p class="notice-text">اگر شما خودتان این حساب را بازیابی نکرده‌اید، ممکن است شخص دیگری اطلاعات ورود شما را بداند — بلافاصله رمز عبور خود را تغییر دهید.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000009'
    WHERE [Id] = '42000000-0000-0000-0000-000000000009';

    PRINT 'Created account-deletion-cancelled template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'account-deletion-cancelled template already exists';
END
GO

-- ============================================================
-- Template 10: account-deletion-completed (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000010')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000010', '40000000-0000-0000-0000-000000000010', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000010', '42000000-0000-0000-0000-000000000010', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0010-000000000001', '43000000-0000-0000-0000-000000000010', N'en', N'Your Account Has Been Deleted',
N'<div class="header">
    <p class="eyebrow">Account deletion</p>
    <h1>Goodbye</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">Your account and its personal data have been permanently deleted, as you requested. This address will receive no further messages from us.</p>
<div class="notice">
    <p class="notice-title">Please note</p>
    <p class="notice-text">For legal and security reasons, a minimal anonymized destruction record is retained in line with our retention policy. Thank you for having been with {{ Platform.Name }}.</p>
</div>'),
    ('44000000-0000-0000-0010-000000000002', '43000000-0000-0000-0000-000000000010', N'ar', N'تم حذف حسابك',
N'<div class="header">
    <p class="eyebrow">حذف الحساب</p>
    <h1>وداعًا</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">تم حذف حسابك وبياناتك الشخصية نهائيًا بناءً على طلبك. لن يتلقى هذا العنوان أي رسائل أخرى منا.</p>
<div class="notice">
    <p class="notice-title">يرجى الملاحظة</p>
    <p class="notice-text">لأسباب قانونية وأمنية، نحتفظ بحد أدنى من سجل إتلاف مجهول الهوية وفقًا لسياسة الاحتفاظ لدينا. شكرًا لأنك كنت معنا في {{ Platform.Name }}.</p>
</div>'),
    ('44000000-0000-0000-0010-000000000003', '43000000-0000-0000-0000-000000000010', N'tr', N'Hesabınız Silindi',
N'<div class="header">
    <p class="eyebrow">Hesap silme</p>
    <h1>Hoşça kalın</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">Talebiniz üzerine hesabınız ve kişisel verileriniz kalıcı olarak silindi. Bu adres bizden başka mesaj almayacaktır.</p>
<div class="notice">
    <p class="notice-title">Lütfen dikkat</p>
    <p class="notice-text">Yasal ve güvenlik nedenleriyle, saklama politikamız doğrultusunda asgari düzeyde anonimleştirilmiş bir imha kaydı tutulur. {{ Platform.Name }} ile olduğunuz için teşekkür ederiz.</p>
</div>'),
    ('44000000-0000-0000-0010-000000000004', '43000000-0000-0000-0000-000000000010', N'fr', N'Votre compte a été supprimé',
N'<div class="header">
    <p class="eyebrow">Suppression du compte</p>
    <h1>Au revoir</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Votre compte et ses données personnelles ont été définitivement supprimés, conformément à votre demande. Cette adresse ne recevra plus aucun message de notre part.</p>
<div class="notice">
    <p class="notice-title">À noter</p>
    <p class="notice-text">Pour des raisons légales et de sécurité, un enregistrement de destruction minimal et anonymisé est conservé conformément à notre politique de rétention. Merci d''avoir fait partie de {{ Platform.Name }}.</p>
</div>'),
    ('44000000-0000-0000-0010-000000000005', '43000000-0000-0000-0000-000000000010', N'zh', N'您的账户已被删除',
N'<div class="header">
    <p class="eyebrow">账户删除</p>
    <h1>再见</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">应您的请求，您的账户及其个人数据已被永久删除。此地址将不会再收到我们的任何消息。</p>
<div class="notice">
    <p class="notice-title">请注意</p>
    <p class="notice-text">出于法律和安全原因，我们将按照保留政策保留最少量的匿名销毁记录。感谢您曾与 {{ Platform.Name }} 同行。</p>
</div>'),
    ('44000000-0000-0000-0010-000000000006', '43000000-0000-0000-0000-000000000010', N'ur', N'آپ کا اکاؤنٹ حذف کر دیا گیا ہے',
N'<div class="header">
    <p class="eyebrow">اکاؤنٹ کا حذف</p>
    <h1>الوداع</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">آپ کی درخواست کے مطابق آپ کا اکاؤنٹ اور اس کا ذاتی ڈیٹا مستقل طور پر حذف کر دیا گیا ہے۔ اس پتے پر ہماری طرف سے مزید کوئی پیغام موصول نہیں ہوگا۔</p>
<div class="notice">
    <p class="notice-title">براہ کرم نوٹ کریں</p>
    <p class="notice-text">قانونی اور حفاظتی وجوہات کی بنا پر، ہماری برقراری پالیسی کے مطابق کم سے کم گمنام شدہ اتلاف کا ریکارڈ محفوظ رکھا جاتا ہے۔ {{ Platform.Name }} کے ساتھ رہنے کا شکریہ۔</p>
</div>'),
    ('44000000-0000-0000-0010-000000000007', '43000000-0000-0000-0000-000000000010', N'fa', N'حساب شما حذف شد',
N'<div class="header">
    <p class="eyebrow">حذف حساب</p>
    <h1>بدرود</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">بنا به درخواست شما، حساب و داده‌های شخصی آن برای همیشه حذف شد. این آدرس دیگر هیچ پیامی از ما دریافت نخواهد کرد.</p>
<div class="notice">
    <p class="notice-title">لطفاً توجه کنید</p>
    <p class="notice-text">به دلایل قانونی و امنیتی، حداقلی از سابقه امحای ناشناس‌شده مطابق با سیاست نگهداری ما حفظ می‌شود. از این‌که با {{ Platform.Name }} بودید سپاسگزاریم.</p>
</div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000010'
    WHERE [Id] = '42000000-0000-0000-0000-000000000010';

    PRINT 'Created account-deletion-completed template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'account-deletion-completed template already exists';
END
GO

-- ============================================================
-- Template 11: privacy-policy-updated (global, Email channel)
-- ============================================================
DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000011')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000011', '40000000-0000-0000-0000-000000000011', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000011', '42000000-0000-0000-0000-000000000011', 1, N'Initial version (SEBAKHI-brand design)', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0011-000000000001', '43000000-0000-0000-0000-000000000011', N'en', N'Our Privacy Policy Is Changing',
N'<div class="header">
    <p class="eyebrow">Privacy policy</p>
    <h1>Our privacy policy is changing</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">We have updated our privacy policy to version <strong>{{ PolicyVersion }}</strong>, effective <strong>{{ EffectiveDate }}</strong>. Please review what changed — it explains what data we hold about you, how long we keep it, and how you can delete your account.</p>
<div class="button-container">
    <a class="button" href="{{ PolicyLink }}">Read the updated policy</a>
</div>
<p class="link-fallback">If the button does not work, copy and paste this link into your browser:</p>
<div class="link-box"><a href="{{ PolicyLink }}">{{ PolicyLink }}</a></div>'),
    ('44000000-0000-0000-0011-000000000002', '43000000-0000-0000-0000-000000000011', N'ar', N'سياسة الخصوصية لدينا تتغير',
N'<div class="header">
    <p class="eyebrow">سياسة الخصوصية</p>
    <h1>سياسة الخصوصية لدينا تتغير</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">لقد حدّثنا سياسة الخصوصية لدينا إلى الإصدار <strong>{{ PolicyVersion }}</strong>، وتسري اعتبارًا من <strong>{{ EffectiveDate }}</strong>. يرجى الاطلاع على ما تغيّر — فهي توضح ما نحتفظ به من بيانات عنك، ومدة الاحتفاظ بها، وكيف يمكنك حذف حسابك.</p>
<div class="button-container">
    <a class="button" href="{{ PolicyLink }}">قراءة السياسة المحدّثة</a>
</div>
<p class="link-fallback">إذا لم يعمل الزر، فانسخ هذا الرابط والصقه في متصفحك:</p>
<div class="link-box"><a href="{{ PolicyLink }}">{{ PolicyLink }}</a></div>'),
    ('44000000-0000-0000-0011-000000000003', '43000000-0000-0000-0000-000000000011', N'tr', N'Gizlilik Politikamız Değişiyor',
N'<div class="header">
    <p class="eyebrow">Gizlilik politikası</p>
    <h1>Gizlilik politikamız değişiyor</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">Gizlilik politikamızı <strong>{{ PolicyVersion }}</strong> sürümüne güncelledik; <strong>{{ EffectiveDate }}</strong> tarihinde yürürlüğe girecek. Lütfen nelerin değiştiğini inceleyin — politika, hakkınızda hangi verileri tuttuğumuzu, ne kadar süreyle sakladığımızı ve hesabınızı nasıl silebileceğinizi açıklar.</p>
<div class="button-container">
    <a class="button" href="{{ PolicyLink }}">Güncellenen politikayı okuyun</a>
</div>
<p class="link-fallback">Düğme çalışmazsa bu bağlantıyı kopyalayıp tarayıcınıza yapıştırın:</p>
<div class="link-box"><a href="{{ PolicyLink }}">{{ PolicyLink }}</a></div>'),
    ('44000000-0000-0000-0011-000000000004', '43000000-0000-0000-0000-000000000011', N'fr', N'Notre politique de confidentialité évolue',
N'<div class="header">
    <p class="eyebrow">Politique de confidentialité</p>
    <h1>Notre politique de confidentialité évolue</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Nous avons mis à jour notre politique de confidentialité vers la version <strong>{{ PolicyVersion }}</strong>, applicable à compter du <strong>{{ EffectiveDate }}</strong>. Veuillez consulter les changements — elle explique quelles données nous détenons à votre sujet, combien de temps nous les conservons et comment supprimer votre compte.</p>
<div class="button-container">
    <a class="button" href="{{ PolicyLink }}">Lire la politique mise à jour</a>
</div>
<p class="link-fallback">Si le bouton ne fonctionne pas, copiez-collez ce lien dans votre navigateur :</p>
<div class="link-box"><a href="{{ PolicyLink }}">{{ PolicyLink }}</a></div>'),
    ('44000000-0000-0000-0011-000000000005', '43000000-0000-0000-0000-000000000011', N'zh', N'我们的隐私政策即将变更',
N'<div class="header">
    <p class="eyebrow">隐私政策</p>
    <h1>我们的隐私政策即将变更</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">我们已将隐私政策更新至 <strong>{{ PolicyVersion }}</strong> 版，自 <strong>{{ EffectiveDate }}</strong> 起生效。请查看变更内容——它说明了我们持有您的哪些数据、保存多长时间，以及如何删除您的账户。</p>
<div class="button-container">
    <a class="button" href="{{ PolicyLink }}">阅读更新后的政策</a>
</div>
<p class="link-fallback">如果按钮无法使用，请将此链接复制并粘贴到浏览器中：</p>
<div class="link-box"><a href="{{ PolicyLink }}">{{ PolicyLink }}</a></div>'),
    ('44000000-0000-0000-0011-000000000006', '43000000-0000-0000-0000-000000000011', N'ur', N'ہماری رازداری کی پالیسی تبدیل ہو رہی ہے',
N'<div class="header">
    <p class="eyebrow">رازداری کی پالیسی</p>
    <h1>ہماری رازداری کی پالیسی تبدیل ہو رہی ہے</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">ہم نے اپنی رازداری کی پالیسی کو ورژن <strong>{{ PolicyVersion }}</strong> پر اپ ڈیٹ کر دیا ہے، جو <strong>{{ EffectiveDate }}</strong> سے نافذ ہو گی۔ براہِ کرم دیکھیں کہ کیا تبدیل ہوا — اس میں بتایا گیا ہے کہ ہم آپ کے بارے میں کون سا ڈیٹا رکھتے ہیں، کتنے عرصے رکھتے ہیں، اور آپ اپنا اکاؤنٹ کیسے حذف کر سکتے ہیں۔</p>
<div class="button-container">
    <a class="button" href="{{ PolicyLink }}">اپ ڈیٹ شدہ پالیسی پڑھیں</a>
</div>
<p class="link-fallback">اگر بٹن کام نہ کرے تو یہ لنک کاپی کر کے اپنے براؤزر میں چسپاں کریں:</p>
<div class="link-box"><a href="{{ PolicyLink }}">{{ PolicyLink }}</a></div>'),
    ('44000000-0000-0000-0011-000000000007', '43000000-0000-0000-0000-000000000011', N'fa', N'سیاست حریم خصوصی ما در حال تغییر است',
N'<div class="header">
    <p class="eyebrow">سیاست حریم خصوصی</p>
    <h1>سیاست حریم خصوصی ما در حال تغییر است</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">ما سیاست حریم خصوصی خود را به نسخه <strong>{{ PolicyVersion }}</strong> به‌روزرسانی کرده‌ایم که از <strong>{{ EffectiveDate }}</strong> اجرا می‌شود. لطفاً تغییرات را بررسی کنید — این سند توضیح می‌دهد چه داده‌هایی از شما نگه می‌داریم، چه مدت نگه می‌داریم و چگونه می‌توانید حساب خود را حذف کنید.</p>
<div class="button-container">
    <a class="button" href="{{ PolicyLink }}">خواندن سیاست به‌روزشده</a>
</div>
<p class="link-fallback">اگر دکمه کار نکرد، این پیوند را کپی کرده و در مرورگر خود جای‌گذاری کنید:</p>
<div class="link-box"><a href="{{ PolicyLink }}">{{ PolicyLink }}</a></div>');

    UPDATE [dbo].[NotificationTemplates]
    SET [PublishedVersionId] = '43000000-0000-0000-0000-000000000011'
    WHERE [Id] = '42000000-0000-0000-0000-000000000011';

    PRINT 'Created privacy-policy-updated template (v1 published, 7 translations)';
END
ELSE
BEGIN
    PRINT 'privacy-policy-updated template already exists';
END
GO
