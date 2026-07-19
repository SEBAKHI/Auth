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
