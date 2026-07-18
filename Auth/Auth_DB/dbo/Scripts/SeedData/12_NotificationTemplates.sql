-- Notification Templates Seed Data
-- The three system templates migrated from the legacy code templates (SmtpEmailService +
-- EmailTemplates resx), each with version 1 published and all 7 language translations.
-- Guarded per template id so admin-created versions are never clobbered on re-publish.
-- BodyText is left NULL everywhere: the plain-text alternative is derived from BodyHtml.

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- ============================================================
-- Template 1: email-verification (global, Email channel)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplates] WHERE [Id] = '42000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[NotificationTemplates] ([Id], [NotificationTypeId], [ApplicationId], [Channel], [DefaultLanguage], [CreatedAt], [CreatedBy])
    VALUES ('42000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', NULL, 1, N'en', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateVersions] ([Id], [TemplateId], [VersionNumber], [ChangeNote], [CreatedAt], [CreatedBy])
    VALUES ('43000000-0000-0000-0000-000000000001', '42000000-0000-0000-0000-000000000001', 1, N'Initial version migrated from code templates', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0001-000000000001', '43000000-0000-0000-0000-000000000001', N'en', N'Verify Your Email Address',
N'<div class="header">
    <h1>Email Verification</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">Please use the following verification code to confirm your email address:</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<p class="message">This code will expire in {{ ExpirationMinutes }} minutes.</p>
<div class="warning">Security Notice: If you did not request this verification code, please ignore this email. Do not share this code with anyone.</div>'),
    ('44000000-0000-0000-0001-000000000002', '43000000-0000-0000-0000-000000000001', N'ar', N'تأكيد عنوان بريدك الإلكتروني',
N'<div class="header">
    <h1>التحقق من البريد الإلكتروني</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">يرجى استخدام رمز التحقق التالي لتأكيد عنوان بريدك الإلكتروني:</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<p class="message">ستنتهي صلاحية هذا الرمز خلال {{ ExpirationMinutes }} دقائق.</p>
<div class="warning">تنبيه أمني: إذا لم تطلب رمز التحقق هذا، يرجى تجاهل هذا البريد الإلكتروني. لا تشارك هذا الرمز مع أي شخص.</div>'),
    ('44000000-0000-0000-0001-000000000003', '43000000-0000-0000-0000-000000000001', N'tr', N'E-posta Adresinizi Doğrulayın',
N'<div class="header">
    <h1>E-posta Doğrulama</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">E-posta adresinizi onaylamak için lütfen aşağıdaki doğrulama kodunu kullanın:</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<p class="message">Bu kodun süresi {{ ExpirationMinutes }} dakika içinde dolacaktır.</p>
<div class="warning">Güvenlik Uyarısı: Bu doğrulama kodunu siz talep etmediyseniz lütfen bu e-postayı dikkate almayın. Bu kodu kimseyle paylaşmayın.</div>'),
    ('44000000-0000-0000-0001-000000000004', '43000000-0000-0000-0000-000000000001', N'fr', N'Vérifiez votre adresse e-mail',
N'<div class="header">
    <h1>Vérification de l''adresse e-mail</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Veuillez utiliser le code de vérification suivant pour confirmer votre adresse e-mail :</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<p class="message">Ce code expirera dans {{ ExpirationMinutes }} minutes.</p>
<div class="warning">Avis de sécurité : Si vous n''avez pas demandé ce code de vérification, veuillez ignorer cet e-mail. Ne partagez ce code avec personne.</div>'),
    ('44000000-0000-0000-0001-000000000005', '43000000-0000-0000-0000-000000000001', N'zh', N'验证您的邮箱地址',
N'<div class="header">
    <h1>邮箱验证</h1>
</div>
<p class="message">您好 {{ UserName }}，</p>
<p class="message">请使用以下验证码确认您的邮箱地址：</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<p class="message">此验证码将在{{ ExpirationMinutes }}分钟后过期。</p>
<div class="warning">安全提示：如果您未请求此验证码，请忽略此邮件。请勿与任何人分享此验证码。</div>'),
    ('44000000-0000-0000-0001-000000000006', '43000000-0000-0000-0000-000000000001', N'ur', N'اپنا ای میل ایڈریس تصدیق کریں',
N'<div class="header">
    <h1>ای میل کی تصدیق</h1>
</div>
<p class="message">السلام علیکم {{ UserName }}،</p>
<p class="message">براہ کرم اپنا ای میل ایڈریس تصدیق کرنے کے لیے درج ذیل تصدیقی کوڈ استعمال کریں:</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<p class="message">یہ کوڈ {{ ExpirationMinutes }} منٹ میں ختم ہو جائے گا۔</p>
<div class="warning">حفاظتی نوٹس: اگر آپ نے یہ تصدیقی کوڈ نہیں مانگا تو براہ کرم اس ای میل کو نظر انداز کریں۔ یہ کوڈ کسی کے ساتھ شیئر نہ کریں۔</div>'),
    ('44000000-0000-0000-0001-000000000007', '43000000-0000-0000-0000-000000000001', N'fa', N'تأیید آدرس ایمیل شما',
N'<div class="header">
    <h1>تأیید ایمیل</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">لطفاً از کد تأیید زیر برای تأیید آدرس ایمیل خود استفاده کنید:</p>
<div class="code-container">
    <div class="otp-code">{{ OtpCode }}</div>
</div>
<p class="message">این کد تا {{ ExpirationMinutes }} دقیقه دیگر منقضی خواهد شد.</p>
<div class="warning">هشدار امنیتی: اگر شما این کد تأیید را درخواست نکرده‌اید، لطفاً این ایمیل را نادیده بگیرید. این کد را با هیچ‌کس به اشتراک نگذارید.</div>');

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
    VALUES ('43000000-0000-0000-0000-000000000002', '42000000-0000-0000-0000-000000000002', 1, N'Initial version migrated from code templates', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0002-000000000001', '43000000-0000-0000-0000-000000000002', N'en', N'Reset Your Password',
N'<div class="header">
    <h1>Password Reset</h1>
</div>
<p class="message">Hello {{ UserName }},</p>
<p class="message">We received a request to reset your password. Click the button below to choose a new password:</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">Reset Password</a>
</div>
<p class="link-fallback">If the button does not work, copy and paste this link into your browser:<br><a href="{{ ResetLink }}">{{ ResetLink }}</a></p>
<p class="message">This link will expire in {{ ExpirationMinutes }} minutes.</p>
<div class="warning">Security Notice: If you did not request a password reset, please ignore this email. Your password will remain unchanged.</div>'),
    ('44000000-0000-0000-0002-000000000002', '43000000-0000-0000-0000-000000000002', N'ar', N'إعادة تعيين كلمة المرور',
N'<div class="header">
    <h1>إعادة تعيين كلمة المرور</h1>
</div>
<p class="message">مرحبًا {{ UserName }}،</p>
<p class="message">تلقينا طلبًا لإعادة تعيين كلمة المرور الخاصة بك. انقر على الزر أدناه لاختيار كلمة مرور جديدة:</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">إعادة تعيين كلمة المرور</a>
</div>
<p class="link-fallback">إذا لم يعمل الزر، انسخ الرابط التالي والصقه في متصفحك:<br><a href="{{ ResetLink }}">{{ ResetLink }}</a></p>
<p class="message">ستنتهي صلاحية هذا الرابط خلال {{ ExpirationMinutes }} دقيقة.</p>
<div class="warning">تنبيه أمني: إذا لم تطلب إعادة تعيين كلمة المرور، يرجى تجاهل هذا البريد الإلكتروني. ستبقى كلمة المرور الخاصة بك دون تغيير.</div>'),
    ('44000000-0000-0000-0002-000000000003', '43000000-0000-0000-0000-000000000002', N'tr', N'Şifrenizi Sıfırlayın',
N'<div class="header">
    <h1>Şifre Sıfırlama</h1>
</div>
<p class="message">Merhaba {{ UserName }},</p>
<p class="message">Şifrenizi sıfırlama talebi aldık. Yeni bir şifre belirlemek için aşağıdaki düğmeye tıklayın:</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">Şifreyi Sıfırla</a>
</div>
<p class="link-fallback">Düğme çalışmıyorsa bu bağlantıyı kopyalayıp tarayıcınıza yapıştırın:<br><a href="{{ ResetLink }}">{{ ResetLink }}</a></p>
<p class="message">Bu bağlantının süresi {{ ExpirationMinutes }} dakika içinde dolacaktır.</p>
<div class="warning">Güvenlik Uyarısı: Şifre sıfırlama talebinde bulunmadıysanız lütfen bu e-postayı dikkate almayın. Şifreniz değişmeden kalacaktır.</div>'),
    ('44000000-0000-0000-0002-000000000004', '43000000-0000-0000-0000-000000000002', N'fr', N'Réinitialisez votre mot de passe',
N'<div class="header">
    <h1>Réinitialisation du mot de passe</h1>
</div>
<p class="message">Bonjour {{ UserName }},</p>
<p class="message">Nous avons reçu une demande de réinitialisation de votre mot de passe. Cliquez sur le bouton ci-dessous pour choisir un nouveau mot de passe :</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">Réinitialiser le mot de passe</a>
</div>
<p class="link-fallback">Si le bouton ne fonctionne pas, copiez et collez ce lien dans votre navigateur :<br><a href="{{ ResetLink }}">{{ ResetLink }}</a></p>
<p class="message">Ce lien expirera dans {{ ExpirationMinutes }} minutes.</p>
<div class="warning">Avis de sécurité : Si vous n''avez pas demandé de réinitialisation de mot de passe, veuillez ignorer cet e-mail. Votre mot de passe restera inchangé.</div>'),
    ('44000000-0000-0000-0002-000000000005', '43000000-0000-0000-0000-000000000002', N'zh', N'重置您的密码',
N'<div class="header">
    <h1>密码重置</h1>
</div>
<p class="message">您好，{{ UserName }}：</p>
<p class="message">我们收到了重置您密码的请求。请点击下方按钮设置新密码：</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">重置密码</a>
</div>
<p class="link-fallback">如果按钮无法使用，请将以下链接复制并粘贴到浏览器中：<br><a href="{{ ResetLink }}">{{ ResetLink }}</a></p>
<p class="message">此链接将在{{ ExpirationMinutes }}分钟后失效。</p>
<div class="warning">安全提示：如果您未请求重置密码，请忽略此邮件。您的密码将保持不变。</div>'),
    ('44000000-0000-0000-0002-000000000006', '43000000-0000-0000-0000-000000000002', N'ur', N'اپنا پاس ورڈ ری سیٹ کریں',
N'<div class="header">
    <h1>پاس ورڈ ری سیٹ</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">ہمیں آپ کا پاس ورڈ ری سیٹ کرنے کی درخواست موصول ہوئی ہے۔ نیا پاس ورڈ منتخب کرنے کے لیے نیچے دیے گئے بٹن پر کلک کریں:</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">پاس ورڈ ری سیٹ کریں</a>
</div>
<p class="link-fallback">اگر بٹن کام نہ کرے تو یہ لنک کاپی کر کے اپنے براؤزر میں پیسٹ کریں:<br><a href="{{ ResetLink }}">{{ ResetLink }}</a></p>
<p class="message">اس لنک کی میعاد {{ ExpirationMinutes }} منٹ میں ختم ہو جائے گی۔</p>
<div class="warning">حفاظتی نوٹس: اگر آپ نے پاس ورڈ ری سیٹ کی درخواست نہیں کی تو براہ کرم اس ای میل کو نظر انداز کریں۔ آپ کا پاس ورڈ تبدیل نہیں ہوگا۔</div>'),
    ('44000000-0000-0000-0002-000000000007', '43000000-0000-0000-0000-000000000002', N'fa', N'بازنشانی رمز عبور',
N'<div class="header">
    <h1>بازنشانی رمز عبور</h1>
</div>
<p class="message">سلام {{ UserName }}،</p>
<p class="message">درخواستی برای بازنشانی رمز عبور شما دریافت کردیم. برای انتخاب رمز عبور جدید روی دکمه زیر کلیک کنید:</p>
<div class="button-container">
    <a class="button" href="{{ ResetLink }}">بازنشانی رمز عبور</a>
</div>
<p class="link-fallback">اگر دکمه کار نکرد، این پیوند را کپی کرده و در مرورگر خود جای‌گذاری کنید:<br><a href="{{ ResetLink }}">{{ ResetLink }}</a></p>
<p class="message">این پیوند تا {{ ExpirationMinutes }} دقیقه دیگر منقضی می‌شود.</p>
<div class="warning">هشدار امنیتی: اگر شما درخواست بازنشانی رمز عبور نکرده‌اید، لطفاً این ایمیل را نادیده بگیرید. رمز عبور شما بدون تغییر باقی می‌ماند.</div>');

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
    VALUES ('43000000-0000-0000-0000-000000000003', '42000000-0000-0000-0000-000000000003', 1, N'Initial version migrated from code templates', GETUTCDATE(), @SystemUserId);

    INSERT INTO [dbo].[NotificationTemplateTranslations] ([Id], [VersionId], [LanguageCode], [Subject], [BodyHtml])
    VALUES
    ('44000000-0000-0000-0003-000000000001', '43000000-0000-0000-0000-000000000003', N'en', N'You''re Invited to Join {{ OrganizationName }}',
N'<div class="header">
    <h1>Organization Invitation</h1>
</div>
<p class="message">Hello,</p>
<p class="message">{{ InviterName }} has invited you to join {{ OrganizationName }}.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">Accept Invitation</a>
</div>
<p class="link-fallback">If the button does not work, copy and paste this link into your browser:<br><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></p>
<p class="message">Or enter this invitation code on the invitation page:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="message">This invitation expires on {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC.</p>
<div class="warning">Security Notice: If you were not expecting this invitation, please ignore this email.</div>'),
    ('44000000-0000-0000-0003-000000000002', '43000000-0000-0000-0000-000000000003', N'ar', N'أنت مدعو للانضمام إلى {{ OrganizationName }}',
N'<div class="header">
    <h1>دعوة إلى مؤسسة</h1>
</div>
<p class="message">مرحبًا،</p>
<p class="message">قام {{ InviterName }} بدعوتك للانضمام إلى {{ OrganizationName }}.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">قبول الدعوة</a>
</div>
<p class="link-fallback">إذا لم يعمل الزر، انسخ الرابط التالي والصقه في متصفحك:<br><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></p>
<p class="message">أو أدخل رمز الدعوة التالي في صفحة الدعوة:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="message">تنتهي صلاحية هذه الدعوة في {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC.</p>
<div class="warning">تنبيه أمني: إذا لم تكن تتوقع هذه الدعوة، يرجى تجاهل هذا البريد الإلكتروني.</div>'),
    ('44000000-0000-0000-0003-000000000003', '43000000-0000-0000-0000-000000000003', N'tr', N'{{ OrganizationName }} organizasyonuna katılmaya davet edildiniz',
N'<div class="header">
    <h1>Organizasyon Daveti</h1>
</div>
<p class="message">Merhaba,</p>
<p class="message">{{ InviterName }}, sizi {{ OrganizationName }} organizasyonuna katılmaya davet etti.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">Daveti Kabul Et</a>
</div>
<p class="link-fallback">Düğme çalışmıyorsa bu bağlantıyı kopyalayıp tarayıcınıza yapıştırın:<br><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></p>
<p class="message">Veya bu davet kodunu davet sayfasına girin:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="message">Bu davetin süresi {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC tarihinde dolacaktır.</p>
<div class="warning">Güvenlik Uyarısı: Bu daveti beklemiyorduysanız lütfen bu e-postayı dikkate almayın.</div>'),
    ('44000000-0000-0000-0003-000000000004', '43000000-0000-0000-0000-000000000003', N'fr', N'Vous êtes invité à rejoindre {{ OrganizationName }}',
N'<div class="header">
    <h1>Invitation à une organisation</h1>
</div>
<p class="message">Bonjour,</p>
<p class="message">{{ InviterName }} vous a invité à rejoindre {{ OrganizationName }}.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">Accepter l''invitation</a>
</div>
<p class="link-fallback">Si le bouton ne fonctionne pas, copiez et collez ce lien dans votre navigateur :<br><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></p>
<p class="message">Ou saisissez ce code d''invitation sur la page d''invitation :</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="message">Cette invitation expire le {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC.</p>
<div class="warning">Avis de sécurité : Si vous n''attendiez pas cette invitation, veuillez ignorer cet e-mail.</div>'),
    ('44000000-0000-0000-0003-000000000005', '43000000-0000-0000-0000-000000000003', N'zh', N'邀请您加入{{ OrganizationName }}',
N'<div class="header">
    <h1>组织邀请</h1>
</div>
<p class="message">您好：</p>
<p class="message">{{ InviterName }}邀请您加入{{ OrganizationName }}。</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">接受邀请</a>
</div>
<p class="link-fallback">如果按钮无法使用，请将以下链接复制并粘贴到浏览器中：<br><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></p>
<p class="message">或在邀请页面输入此邀请码：</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="message">此邀请将于{{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC失效。</p>
<div class="warning">安全提示：如果您并未预期收到此邀请，请忽略此邮件。</div>'),
    ('44000000-0000-0000-0003-000000000006', '43000000-0000-0000-0000-000000000003', N'ur', N'آپ کو {{ OrganizationName }} میں شامل ہونے کی دعوت دی گئی ہے',
N'<div class="header">
    <h1>تنظیم کی دعوت</h1>
</div>
<p class="message">سلام،</p>
<p class="message">{{ InviterName }} نے آپ کو {{ OrganizationName }} میں شامل ہونے کی دعوت دی ہے۔</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">دعوت قبول کریں</a>
</div>
<p class="link-fallback">اگر بٹن کام نہ کرے تو یہ لنک کاپی کر کے اپنے براؤزر میں پیسٹ کریں:<br><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></p>
<p class="message">یا یہ دعوتی کوڈ دعوت کے صفحے پر درج کریں:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="message">اس دعوت کی میعاد {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC کو ختم ہو جائے گی۔</p>
<div class="warning">حفاظتی نوٹس: اگر آپ کو اس دعوت کی توقع نہیں تھی تو براہ کرم اس ای میل کو نظر انداز کریں۔</div>'),
    ('44000000-0000-0000-0003-000000000007', '43000000-0000-0000-0000-000000000003', N'fa', N'شما برای پیوستن به {{ OrganizationName }} دعوت شده‌اید',
N'<div class="header">
    <h1>دعوت‌نامه سازمان</h1>
</div>
<p class="message">سلام،</p>
<p class="message">{{ InviterName }} شما را برای پیوستن به {{ OrganizationName }} دعوت کرده است.</p>
<div class="button-container">
    <a class="button" href="{{ InvitationLink }}">پذیرش دعوت</a>
</div>
<p class="link-fallback">اگر دکمه کار نکرد، این پیوند را کپی کرده و در مرورگر خود جای‌گذاری کنید:<br><a href="{{ InvitationLink }}">{{ InvitationLink }}</a></p>
<p class="message">یا این کد دعوت را در صفحه دعوت وارد کنید:</p>
<div class="code-container">
    <div class="token-code">{{ InvitationToken }}</div>
</div>
<p class="message">این دعوت‌نامه در {{ ExpiresAt | date: "%Y-%m-%d %H:%M" }} UTC منقضی می‌شود.</p>
<div class="warning">هشدار امنیتی: اگر انتظار این دعوت‌نامه را نداشتید، لطفاً این ایمیل را نادیده بگیرید.</div>');

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
