-- ============================================================================
-- 2026-08-04 — Bounded identifier reservation + honest retention wording
--
-- The seed scripts guard every insert with IF NOT EXISTS, so an already-seeded
-- database keeps its original rows and never sees a corrected seed. This script
-- is how the corrections reach an existing deployment. It is idempotent and can
-- be run more than once.
--
-- What it changes and why:
--
--   1. The published privacy policy and the deletion-completed e-mail claimed
--      the surviving destruction record is ANONYMOUS. It is not: the stored
--      value is an HMAC of the address under a key this system keeps, which
--      makes it pseudonymous — anyone holding the key can test a candidate
--      address against it, which is exactly what the reservation check does on
--      every registration. Both texts now say what the system actually does.
--
--   2. The same record was published as retained PERMANENTLY. Permanent
--      retention of a re-identifiable value could not be reconciled with the
--      anonymity claim, so the reservation is now bounded and the tombstone is
--      swept when the window ends. The published period is interpolated from
--      AccountDeletion:IdentifierReservationDays at read time.
--
--   3. UsernameHash is dropped. Nothing ever read it — the reservation guard
--      checks the e-mail digest only — and the username is derived rather than
--      chosen by the user, so the column stored a second identifier of a person
--      for a check that did not exist.
--
--   4. KeyVersion is added. Without it the identifier HMAC key can never be
--      rotated: digests written under an old key are indistinguishable from
--      current ones, so every reservation would silently stop matching.
-- ============================================================================

SET XACT_ABORT ON;
GO

-- ---------------------------------------------------------------------------
-- 1) Schema: drop the unread username reservation, add the key version.
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.tables WHERE [name] = 'AccountDeletionTombstones')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes
               WHERE [name] = 'IX_AccountDeletionTombstones_UsernameHash')
    BEGIN
        DROP INDEX [IX_AccountDeletionTombstones_UsernameHash]
            ON [dbo].[AccountDeletionTombstones];
        PRINT 'Dropped IX_AccountDeletionTombstones_UsernameHash.';
    END

    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE [object_id] = OBJECT_ID('dbo.AccountDeletionTombstones')
                 AND [name] = 'UsernameHash')
    BEGIN
        ALTER TABLE [dbo].[AccountDeletionTombstones] DROP COLUMN [UsernameHash];
        PRINT 'Dropped AccountDeletionTombstones.UsernameHash.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE [object_id] = OBJECT_ID('dbo.AccountDeletionTombstones')
                     AND [name] = 'KeyVersion')
    BEGIN
        ALTER TABLE [dbo].[AccountDeletionTombstones]
            ADD [KeyVersion] TINYINT NOT NULL
                CONSTRAINT [DF_AccountDeletionTombstones_KeyVersion] DEFAULT 1;
        PRINT 'Added AccountDeletionTombstones.KeyVersion.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE [name] = 'IX_AccountDeletionTombstones_DeletedAtUtc')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_AccountDeletionTombstones_DeletedAtUtc]
        ON [dbo].[AccountDeletionTombstones] ([DeletedAtUtc]);
        PRINT 'Added IX_AccountDeletionTombstones_DeletedAtUtc for the retention sweep.';
    END
END
ELSE
BEGIN
    -- The table ships via the DACPAC only; there is no create-if-missing path
    -- here on purpose. If it is absent, the DACPAC publish did not reach this
    -- database and account deletion cannot work at all — fix that first.
    RAISERROR('AccountDeletionTombstones is missing. Publish the DACPAC before running this script.', 16, 1);
END
GO

-- ---------------------------------------------------------------------------
-- 2) Deletion-completed e-mail: drop the false "anonymized" claim.
--    Applied per language against the CURRENT published body, so a template
--    that was already corrected is left untouched.
-- ---------------------------------------------------------------------------
DECLARE @EmailFixes TABLE ([OldText] NVARCHAR(MAX), [NewText] NVARCHAR(MAX));

INSERT INTO @EmailFixes ([OldText], [NewText]) VALUES
(N'a minimal anonymized destruction record is retained in line with our retention policy.',
 N'a minimal destruction record is retained in line with our retention policy: keyed one-way digests of your email and username, and nothing else.'),
(N'نحتفظ بحد أدنى من سجل إتلاف مجهول الهوية وفقًا لسياسة الاحتفاظ لدينا.',
 N'نحتفظ بحدّ أدنى من سجل الإتلاف وفقًا لسياسة الاحتفاظ لدينا: ملخّصات أحادية الاتجاه بمفتاح لبريدك واسم المستخدم، ولا شيء غير ذلك.'),
(N'saklama politikamız doğrultusunda asgari düzeyde anonimleştirilmiş bir imha kaydı tutulur.',
 N'saklama politikamız doğrultusunda asgari düzeyde bir imha kaydı tutulur: e-postanızın ve kullanıcı adınızın anahtarlı tek yönlü özetleri, başka hiçbir şey değil.'),
(N'un enregistrement de destruction minimal et anonymisé est conservé conformément à notre politique de rétention.',
 N'un enregistrement de destruction minimal est conservé conformément à notre politique de rétention : des condensats unidirectionnels à clé de votre e-mail et de votre nom d''utilisateur, rien de plus.'),
(N'我们将按照保留政策保留最少量的匿名销毁记录。',
 N'我们将按照保留政策保留最少量的销毁记录：您的邮箱和用户名的带密钥单向摘要，仅此而已。'),
(N'ہماری برقراری پالیسی کے مطابق کم سے کم گمنام شدہ اتلاف کا ریکارڈ محفوظ رکھا جاتا ہے۔',
 N'ہماری برقراری پالیسی کے مطابق کم سے کم اتلاف کا ریکارڈ محفوظ رکھا جاتا ہے: آپ کے ای میل اور صارف نام کے کلید والے یک طرفہ ڈائجسٹ، اس کے سوا کچھ نہیں۔'),
(N'حداقلی از سابقه امحای ناشناس‌شده مطابق با سیاست نگهداری ما حفظ می‌شود.',
 N'حداقلی از سابقه امحا مطابق با سیاست نگهداری ما حفظ می‌شود: چکیده‌های یک‌طرفه با کلید از ایمیل و نام کاربری شما، و نه چیز دیگری.');

DECLARE @OldText NVARCHAR(MAX), @NewText NVARCHAR(MAX), @Fixed INT = 0;
DECLARE fix_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT [OldText], [NewText] FROM @EmailFixes;

OPEN fix_cursor;
FETCH NEXT FROM fix_cursor INTO @OldText, @NewText;

WHILE @@FETCH_STATUS = 0
BEGIN
    UPDATE [dbo].[NotificationTemplateTranslations]
    SET [BodyHtml] = REPLACE([BodyHtml], @OldText, @NewText)
    WHERE CHARINDEX(@OldText, [BodyHtml]) > 0;

    SET @Fixed = @Fixed + @@ROWCOUNT;
    FETCH NEXT FROM fix_cursor INTO @OldText, @NewText;
END

CLOSE fix_cursor;
DEALLOCATE fix_cursor;

PRINT CONCAT('Deletion-completed e-mail: corrected ', @Fixed, ' template translation(s).');
GO

-- ---------------------------------------------------------------------------
-- 3) Published privacy policy: the retention row for the deletion record.
--    ContentJson is replaced fragment-wise so operator edits elsewhere in the
--    document survive.
-- ---------------------------------------------------------------------------
DECLARE @PolicyFixes TABLE ([OldText] NVARCHAR(MAX), [NewText] NVARCHAR(MAX));

INSERT INTO @PolicyFixes ([OldText], [NewText]) VALUES
(N'"category":"Deletion record (hashed identifiers)","retention":"Permanent","detail":"One-way HMAC digests of the deleted email and username, kept so deleted identifiers can never be re-registered by someone else. Contains no readable personal data."',
 N'"category":"Deletion record (hashed identifier)","retention":"{{identifierReservationDays}} days","detail":"A keyed one-way digest of the deleted email, kept so nobody — including you — can re-register that address while the reservation lasts. An address cannot be read out of a digest, but we keep the key that can test one, so this is a pseudonymous record rather than an anonymous one. It is deleted when the window ends, and the address becomes available again."'),
(N'"category":"سجل الحذف (معرّفات ملخّصة)","retention":"دائم","detail":"ملخصات HMAC أحادية الاتجاه للبريد الإلكتروني واسم المستخدم المحذوفين، يُحتفظ بها حتى لا يتمكن أي شخص آخر أبدًا من إعادة تسجيلهما. لا يتضمن أي بيانات شخصية قابلة للقراءة."',
 N'"category":"سجل الحذف (معرّف ملخّص)","retention":"{{identifierReservationDays}} يومًا","detail":"ملخّص HMAC أحادي الاتجاه بمفتاح للبريد الإلكتروني المحذوف، يُحتفظ به حتى لا يتمكن أحد — بمن فيهم أنت — من إعادة تسجيل ذلك العنوان ما دام الحجز قائمًا. لا يمكن استخراج العنوان من الملخّص، لكننا نحتفظ بالمفتاح الذي يتيح اختبار عنوان معروف، فهو سجل مستعار الهوية لا مجهّل. ويُحذف عند انتهاء المدة، فيعود العنوان متاحًا."'),
(N'"category":"Silme kaydı (özetlenmiş tanımlayıcılar)","retention":"Kalıcı","detail":"Silinen e-posta ve kullanıcı adının tek yönlü HMAC özetleri; silinen tanımlayıcıların bir başkası tarafından asla yeniden kaydedilememesi için tutulur. Okunabilir hiçbir kişisel veri içermez."',
 N'"category":"Silme kaydı (özetlenmiş tanımlayıcı)","retention":"{{identifierReservationDays}} gün","detail":"Silinen e-postanın anahtarlı tek yönlü HMAC özeti; rezervasyon sürdüğü sürece o adresi hiç kimsenin — siz dahil — yeniden kaydedememesi için tutulur. Özetten adres geri okunamaz, ancak bilinen bir adresi sınayabilen anahtar bizde kaldığından bu kayıt anonim değil, takma adlıdır. Süre dolduğunda silinir ve adres yeniden kullanılabilir hâle gelir."'),
(N'"category":"Trace de suppression (identifiants hachés)","retention":"Permanente","detail":"Condensats HMAC à sens unique de l''e-mail et du nom d''utilisateur supprimés, conservés pour que ces identifiants ne puissent jamais être réenregistrés par quelqu''un d''autre. Ne contient aucune donnée personnelle lisible."',
 N'"category":"Trace de suppression (identifiant haché)","retention":"{{identifierReservationDays}} jours","detail":"Condensat HMAC à sens unique et à clé de l''e-mail supprimé, conservé pour que personne — vous y compris — ne puisse réenregistrer cette adresse tant que la réservation dure. Une adresse ne peut pas être lue à partir d''un condensat, mais nous conservons la clé qui permet de tester une adresse connue : cet enregistrement est donc pseudonymisé, et non anonyme. Il est supprimé à l''expiration du délai et l''adresse redevient disponible."'),
(N'"category":"删除记录（哈希化标识符）","retention":"永久","detail":"被删除邮箱和用户名的单向 HMAC 摘要，保留的目的是确保这些标识符永远不会被他人重新注册。不含任何可读的个人数据。"',
 N'"category":"删除记录（哈希化标识符）","retention":"{{identifierReservationDays}} 天","detail":"被删除邮箱的带密钥单向 HMAC 摘要，保留的目的是在保留期内确保任何人（包括您本人）都无法重新注册该地址。无法从摘要还原出地址，但我们保留着可用于验证某个已知地址的密钥，因此该记录属于假名化数据，而非匿名数据。保留期结束后即被删除，该地址重新可用。"'),
(N'"category":"حذف کا ریکارڈ (ہیش شدہ شناختیں)","retention":"مستقل","detail":"حذف شدہ ای میل اور صارف نام کے یک طرفہ HMAC ڈائجسٹ، جو اس لیے رکھے جاتے ہیں کہ یہ شناختیں کبھی کوئی اور دوبارہ رجسٹر نہ کر سکے۔ کوئی قابلِ مطالعہ ذاتی ڈیٹا شامل نہیں۔"',
 N'"category":"حذف کا ریکارڈ (ہیش شدہ شناخت)","retention":"{{identifierReservationDays}} دن","detail":"حذف شدہ ای میل کا کلید والا یک طرفہ HMAC ڈائجسٹ، جو اس لیے رکھا جاتا ہے کہ ریزرویشن کے دوران کوئی بھی — بشمول آپ کے — وہ پتہ دوبارہ رجسٹر نہ کر سکے۔ ڈائجسٹ سے پتہ واپس نہیں پڑھا جا سکتا، لیکن وہ کلید ہمارے پاس رہتی ہے جس سے کسی معلوم پتے کی جانچ ممکن ہے، چنانچہ یہ ریکارڈ گمنام نہیں بلکہ فرضی نام والا ہے۔ مدت ختم ہونے پر یہ حذف ہو جاتا ہے اور پتہ دوبارہ دستیاب ہو جاتا ہے۔"'),
(N'"category":"سابقه حذف (شناسه‌های هش‌شده)","retention":"دائمی","detail":"چکیده‌های HMAC یک‌طرفه از ایمیل و نام کاربری حذف‌شده، که نگه داشته می‌شوند تا این شناسه‌ها هرگز توسط دیگری قابل ثبت مجدد نباشند. هیچ داده شخصی قابل‌خواندنی در بر ندارد."',
 N'"category":"سابقه حذف (شناسه هش‌شده)","retention":"{{identifierReservationDays}} روز","detail":"چکیده HMAC یک‌طرفه با کلید از ایمیل حذف‌شده، که نگه داشته می‌شود تا تا پایان دوره رزرو هیچ‌کس — از جمله خود شما — نتواند آن نشانی را دوباره ثبت کند. نشانی را نمی‌توان از چکیده بازخواند، اما کلیدی که امکان آزمودن یک نشانی معلوم را می‌دهد نزد ما می‌ماند، پس این سابقه ناشناس نیست بلکه با نام مستعار است. در پایان دوره حذف می‌شود و نشانی دوباره در دسترس قرار می‌گیرد."'),
-- French audit-log wording: "anonymisés" overstates what the purge does; every
-- other locale says de-identified, and the French text alone promised a
-- standard the system does not meet.
(N'"retention":"Anonymisé à la suppression du compte"',
 N'"retention":"Dépersonnalisé à la suppression du compte"'),
(N'Purgés automatiquement ; anonymisés immédiatement à la suppression du compte.',
 N'Purgés automatiquement ; dépersonnalisés immédiatement à la suppression du compte.'),
(N'les journaux de sécurité sont anonymisés,',
 N'les journaux de sécurité sont dépersonnalisés,');

DECLARE @PolicyOld NVARCHAR(MAX), @PolicyNew NVARCHAR(MAX), @PolicyFixed INT = 0;
DECLARE policy_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT [OldText], [NewText] FROM @PolicyFixes;

OPEN policy_cursor;
FETCH NEXT FROM policy_cursor INTO @PolicyOld, @PolicyNew;

WHILE @@FETCH_STATUS = 0
BEGIN
    UPDATE [dbo].[PrivacyPolicyTranslations]
    SET [ContentJson] = REPLACE([ContentJson], @PolicyOld, @PolicyNew)
    WHERE CHARINDEX(@PolicyOld, [ContentJson]) > 0;

    SET @PolicyFixed = @PolicyFixed + @@ROWCOUNT;
    FETCH NEXT FROM policy_cursor INTO @PolicyOld, @PolicyNew;
END

CLOSE policy_cursor;
DEALLOCATE policy_cursor;

PRINT CONCAT('Privacy policy: corrected ', @PolicyFixed, ' translation row(s).');
GO

-- ---------------------------------------------------------------------------
-- 3b) Data-controller identity: replace the baked-in placeholders with tokens.
--
--     These were literal text in ContentJson because the seed was generated
--     from the SPA bundle while the values were still unfilled. They are now
--     {{tokens}} interpolated at read time from the DataController settings
--     section, so filling them in the console updates all seven languages at
--     once — and the publish guard refuses to publish while they are blank.
--
--     Run this BEFORE filling the settings, then fill them in the console:
--     System Settings -> Data controller.
-- ---------------------------------------------------------------------------
DECLARE @ControllerTokens TABLE ([OldText] NVARCHAR(200), [NewText] NVARCHAR(200));

INSERT INTO @ControllerTokens ([OldText], [NewText]) VALUES
(N'[LEGAL ENTITY NAME]',      N'{{legalName}}'),
(N'[REGISTERED ADDRESS]',     N'{{address}}'),
(N'[PRIVACY CONTACT EMAIL]',  N'{{privacyEmail}}'),
(N'[EMAIL DELIVERY PROVIDER]', N'{{emailProvider}}'),
(N'[HOSTING PROVIDER]',       N'{{hostingProvider}}'),
(N'[HOSTING COUNTRY]',        N'{{hostingCountry}}');

DECLARE @TokenOld NVARCHAR(200), @TokenNew NVARCHAR(200), @TokenRows INT = 0;
DECLARE token_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT [OldText], [NewText] FROM @ControllerTokens;

OPEN token_cursor;
FETCH NEXT FROM token_cursor INTO @TokenOld, @TokenNew;

WHILE @@FETCH_STATUS = 0
BEGIN
    UPDATE [dbo].[PrivacyPolicyTranslations]
    SET [ContentJson] = REPLACE([ContentJson], @TokenOld, @TokenNew)
    WHERE CHARINDEX(@TokenOld, [ContentJson]) > 0;

    SET @TokenRows = @TokenRows + @@ROWCOUNT;
    FETCH NEXT FROM token_cursor INTO @TokenOld, @TokenNew;
END

CLOSE token_cursor;
DEALLOCATE token_cursor;

PRINT CONCAT('Controller placeholders: tokenised across ', @TokenRows, ' translation row update(s).');
GO

-- ---------------------------------------------------------------------------
-- 4) Verification. Any count above zero means a false claim is still published.
-- ---------------------------------------------------------------------------
SELECT
    (SELECT COUNT(1) FROM [dbo].[PrivacyPolicyTranslations]
     WHERE [ContentJson] LIKE N'%readable personal data%'
        OR [ContentJson] LIKE N'%قابلة للقراءة.%'
        OR [ContentJson] LIKE N'%Okunabilir hiçbir%'
        OR [ContentJson] LIKE N'%personnelle lisible%'
        OR [ContentJson] LIKE N'%可读的个人数据%') AS [PolicyRowsStillClaimingNoReadableData],
    (SELECT COUNT(1) FROM [dbo].[NotificationTemplateTranslations]
     WHERE [BodyHtml] LIKE N'%anonymized destruction%'
        OR [BodyHtml] LIKE N'%مجهول الهوية%'
        OR [BodyHtml] LIKE N'%anonimleştirilmiş%'
        OR [BodyHtml] LIKE N'%匿名销毁%') AS [EmailTranslationsStillClaimingAnonymity],
    (SELECT COUNT(1) FROM sys.columns
     WHERE [object_id] = OBJECT_ID('dbo.AccountDeletionTombstones')
       AND [name] = 'UsernameHash') AS [UsernameHashColumnStillPresent],
    (SELECT COUNT(1) FROM [dbo].[PrivacyPolicyTranslations]
     WHERE [ContentJson] LIKE N'%[[]LEGAL ENTITY NAME]%'
        OR [ContentJson] LIKE N'%[[]REGISTERED ADDRESS]%'
        OR [ContentJson] LIKE N'%[[]PRIVACY CONTACT EMAIL]%'
        OR [ContentJson] LIKE N'%[[]HOSTING COUNTRY]%') AS [PolicyRowsStillHoldingPlaceholders];
GO

-- ---------------------------------------------------------------------------
-- 5) NEXT STEP, and it is not optional.
--
--     The policy now renders the controller from settings, which are EMPTY by
--     default. Until they are filled the public page shows its draft banner and
--     publishing is refused. Fill them in the console:
--
--         System Settings -> Operations -> Data controller
--
--     Required: LegalName, Address, PrivacyEmail, EmailProvider,
--               HostingProvider, HostingCountry (bare country name).
--     Optional: DpoContact, VerbisNo, KepAddress — each omits its own line.
-- ---------------------------------------------------------------------------
