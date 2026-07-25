# مراجعة خصومية: خطة الحذف المنطقي للتطبيقات

## الحالة

مراجعة اختبار إجهاد لخطة `APPLICATION_SOFT_DELETE_IMPLEMENTATION_PLAN.ar.md` — مبنية على استكشاف كامل للكود مع تحقق خصومي مستقل من كل ادعاء حرج (59 ادعاءً جُمع، 8 ادعاءات عالية الخطورة خضعت لمحاولة تفنيد مستقلة).

> **ملحق ما بعد التنفيذ (2026-07-25):** نُفِّذت الخطة المحصنة أدناه مع تعديل واحد جوهري قرره المالك بعد سؤال متابعة: **تقاعد تطبيق المنصة المزروع نهائياً** (`00000000-...-0001` / كود `auth`) بدل حمايته — ثبت بالاستكشاف أن لا شيء في الكود أو الإعدادات أو الواجهة يعتمد عليه، وأن RBAC المنصة ينتقل إلى النطاق العام (`ApplicationId = NULL`) بلا تصادم. لذلك أُسقطت آلية `ProtectedApplicationIds` (§3.4 و§7-a) من التنفيذ — لم يعد ثمة تطبيق منصة يحتاج حماية — وأُضيف ترحيل `2026-07-26_RetirePlatformApplication.sql` الذي يعيد نطاق أدوار وأذونات المنصة إلى NULL ويزيل الصف مادياً بترتيب FK-safe. كل ما عدا ذلك نُفِّذ كما هو موصوف. راجع سجل الفرع `claude/app-deletion-api-keys-ibyz4y`.

## التاريخ

2026-07-24

---

## الجزء الأول: الحكم

**جوهر الخطة صحيح ولا بديل عملي عنه.** التحقق أثبت أن 17 مفتاحاً أجنبياً يشير إلى `Applications`، منها 15 بسلوك `NO ACTION` واثنان فقط بـ`CASCADE` (`AuthorizationCodes`, `ApplicationRedirectUris`). فحوصات الحذف الحالية تغطي 3 علاقات من أصل 15 علاقة مانعة، والتطبيق المزروع يملك صفوف `Roles` و`Permissions` مزروعة تشير إليه — أي أن حذفه المادي **مستحيل رياضياً** حتى بعد إبطال كل المفاتيح. الحذف المنطقي هو المعالجة الجذرية الوحيدة.

**لكن الخطة بصيغتها الحالية تحتوي على:**

1. **ثغرة تصعيد صلاحيات كامنة في ترتيب التنفيذ** (حرجة — القسم 2.1).
2. **باب أحادي الاتجاه بلا مخرج**: إزالة الحماية + لا استعادة + كود محجوز للأبد (القسم 2.2).
3. **افتراض تأسيسي خاطئ عن الحارس المُزال**: الحارس الذي تزيله الخطة كان **كوداً ميتاً أصلاً** — لم يحمِ تطبيق النظام يوماً (القسم 2.3).
4. **نطاق ناقص**: 19 ملفاً تقديرٌ ناقص؛ الصحيح 28 ملفاً، والفارق ليس تفاصيل بل مسارات قراءة وتدقيق كاملة (القسم 2.4).

---

## الجزء الثاني: الاكتشافات المُتحقق منها خصومياً

### 2.1 ثغرة تصعيد الصلاحيات: خطوتا الخطة 3 و6 لا يجوز فصلهما (حرجة)

المسار الحالي في `RefreshTokenCommandHandler.cs:121-136`:

```csharp
string? audience = null;
if (storedToken.ApplicationId.HasValue)
{
    var application = await _applicationRepository.GetByIdAsync(...);
    audience = application?.Code;   // null إذا غاب التطبيق
}
```

وفي `JwtTokenService.cs:123`:

```csharp
Audience = string.IsNullOrEmpty(audience) ? _settings.Audience : audience,
```

اليوم `GetByIdAsync` يعيد الصف دائماً، فالسقوط إلى Audience المنصة غير قابل للوصول عملياً. **لحظة إضافة `IsDeleted = 0` إلى `GetByIdAsync` (قرار الخطة 3) قبل تحصين الـRefresh handler (قرار الخطة 6)، يصبح Refresh Token لتطبيق خارجي محذوف يسكّ توكناً جديداً بجمهور المنصة نفسها** — وهو بالضبط ما يقبله Bearer handler الخاص بـAuth API (`Program.cs:500-501`: `ValidAudience = jwtSettings.Audience`)، مع صلاحيات المستخدم المعاد قراءتها كاملة. ترقيةٌ من "توكن مقيد بتطبيق خارجي" إلى "توكن مقبول لدى API إدارة المنصة".

**الإلزام:** الخطوتان تُشحنان في Commit واحد غير قابل للتجزئة، مع اختبار Regression يثبت أن `GenerateAccessToken` لا يُستدعى إطلاقاً عند غياب/تعطيل التطبيق.

### 2.2 الباب أحادي الاتجاه: الخطة تجعل الخطأ التشغيلي أبدياً

تقاطع أربعة قرارات في الخطة ينتج حالة لا رجعة فيها عبر أي واجهة:

| القرار | الأثر المتراكم |
|---|---|
| إزالة مفهوم تطبيق النظام (5) | التطبيق `00000000-...-0001` قابل للحذف بنقرة |
| الاستعادة خارج النطاق (7) | لا مسار API للتراجع — `Activate/Deactivate` في الكيان **بلا أي مستدعٍ** أصلاً (`Application.cs:230-238`) |
| الكود محجوز للأبد (4) | لا يمكن إنشاء بديل بكود `auth` — `ExistsByCodeAsync` شامل + Collation غير حساسة للحالة |
| البذور لا تُحيي (مُتحقق) | `IF NOT EXISTS (WHERE [Id] = @AuthAppId)` — الصف المحذوف منطقياً يأخذ فرع `ELSE` ولا يُلمس (`Script.PostDeployment.sql:24-26`) |

**ما يتعطل نهائياً عند حذف تطبيق المنصة:** عميل OAuth `client_id=auth` (يُرفض في `AuthorizeCommandHandler.cs:69-74`)، والـBranding العام (`GetPublicBrandingQueryHandler.cs:32-35`)، وقوائم كتالوج RBAC للمنصة (الأدوار والأذونات المزروعة كلها بـ`ApplicationId = @AuthAppId`). الاسترداد الوحيد: SQL يدوي في الإنتاج — وهو نمط الخطأ نفسه الذي أنتج الحادثة الأصلية.

**التحقق من القفل الذاتي (سؤال المراجعة المركزي): لا قفل ذاتي كامل** — تم تتبعه بدقة: تسجيل دخول الكونسول والـRefresh لا يمران بتطبيق `auth` إطلاقاً (`LoginResponseBuilder.cs:70-105` يكتب `ApplicationId = null`؛ الواجهة تستخدم `/api/v1/Auth/login` أول-طرف: `Auth_UI/packages/api/src/client.ts:14-15`)، والتحقق من JWT يستخدم Audience ثابتاً من الإعدادات لا من الجدول. لذا الحذف لا يُسقط الدخول — لكنه يترك تدهوراً دائماً غير قابل للإصلاح عبر API.

### 2.3 الافتراض الخاطئ: الحارس المُزال لم يكن حماية أصلاً

الخطة تصف إزالة الحارس كأنها مقايضة (إزالة حماية مقابل مبدأ Id-only). **الواقع المُتحقق: لا توجد مقايضة — الحارس كود ميت:**

- البذرة تُدخل الكود بحروف صغيرة: `N'auth'` (`Script.PostDeployment.sql:44`).
- الحارس يقارن Ordinal حساس للحالة: `application.Code == "AUTH"` (`DeleteApplicationCommandHandler.cs:34`).
- مسار الترطيب لا يُكبّر الأحرف: `ToEntity()` يمرر `Code` حرفياً (`ApplicationRepository.cs:450-472`)؛ `ToUpperInvariant` موجود فقط في مصنع `Create` الذي لا يمس الصف المزروع (`Application.cs:138`).
- Collation قاعدة البيانات غير حساسة للحالة (`Auth_DB.sqlproj:16`) — لهذا لم تنكشف المفارقة عبر استعلامات SQL قط، **ولهذا أيضاً لا يمكن لأي تطبيق آخر أن يحمل الكود `AUTH` أصلاً** (يصطدم بـ`UQ_Applications_Code` مع الصف المزروع) — أي أن الحارس غير قابل للوصول عالمياً، لا لصف الحادثة فقط.
- الاختبار الوحيد للحارس يفبرك `code: "AUTH"` بحروف كبيرة (`ApplicationCommandHandlerTests.cs:291`) — **الاختبار يوثق الخطأ ويخفيه بدل أن يكشفه.**
- `CannotModifySystemApplication` بلا أي مستدعٍ في الحل كله — تطبيق النظام قابل للتعديل الكامل اليوم.

هذا يفسر الحادثة بالكامل: الحارس صامت، فحص المفاتيح يعدّ `RevokedAt IS NULL` فقط بينما FK يرى كل الصفوف، تعيينات الأدمن المزروعة عالمية بـ`ApplicationId = NULL` فلا يلتقطها `HasActiveUserAssignmentsAsync`، ثم `DELETE` مادي يصطدم بأول FK — و`ExceptionHandlingMiddleware` بلا ذراع لـ`SqlException` فيسقط إلى 500.

**النتيجة الخصومية:** إزالة الحارس صحيحة (إزالة كود ميت)، لكن الاستنتاج بأن لا حاجة لأي حماية استنتاجٌ معكوس — الحادثة نفسها هي الدليل على الحاجة إلى حماية **تعمل فعلاً**. الحل المتوافق مع مبدأ الخطة (لا قرار من اسم أو كود): قائمة `ProtectedApplicationIds` مربوطة من الإعدادات وتُفحص بـ**Id** — بنيوية، قابلة للتوسيع تشغيلياً، وليست استنتاجاً من هوية نصية.

### 2.4 النطاق الناقص: ما فاتته قائمة الـ19 ملفاً

- **مسارات القراءة الثانوية بـSQL خام خارج `ApplicationRepository`** تُظهر التطبيقات المحذوفة إلى الأبد: `DashboardStatsRepository.GetAppActivityAsync` (~334)، `RoleRepository.GetRoleApplicationsAsync` (417-447)، `UserRepository.GetUserApplicationsAsync` (696-747).
- **إثراء سجلات التدقيق ينكسر بصمت**: 4 معالجات AuditLogs و`NameLookupHelper.cs:60-71` تستدعي `GetByIdAsync` وتبتلع `null` — بعد الفلترة تفقد صفوف التدقيق التاريخية اسم التطبيق. مطلوب Seam جديد `GetByIdIncludingDeletedAsync` (54 ملفاً يحقن `IApplicationRepository`؛ مسار التدقيق وحده يستحق الاستثناء).
- **فحص Webhook Keys المذكور في الخطة غير موجود أصلاً**: لا `HasActiveWebhookKeysAsync` في العقد (`IApplicationRepository.cs:67-77`) — الخطة تصفه كإبقاء وهو إنشاء.
- **تحصين مفاتيح API يغلق ثغرة قائمة اليوم**: استعلامات التحقق (`ApiKeyRepository.cs:38-41, 218-223`) لا تربط `Applications` إطلاقاً — مفتاح تطبيقٍ **معطل** (`IsActive = 0`) يتحقق بنجاح اليوم. البند ليس تحصيناً لما بعد الحذف فقط.
- **ذاكرة SDK جانب العميل**: `AuthSystemClient` يخبئ نتيجة التحقق الإيجابية 60 ثانية لمفاتيح API و300 ثانية لمفاتيح Webhook بلا أي Invalidation (`AuthSystemOptions.cs:31,36`) — نافذة قبول متبقية بعد الحذف يجب توثيقها كسلوك مقبول.
- **نافذة التوكنات الصادرة**: 15 دقيقة + 60 ثانية انحراف ≈ 16 دقيقة قبول لدى الخدمات النازلة. إضافة محور "Application" إلى `RevokedTokens` **غير مجدية الآن** — الـBlacklist يعمل داخل Auth API فقط، والتوكنات ذات Audience تطبيقي لا تصل إليه أصلاً.
- **`DeletedBy` في الأمر يُسجَّل في اللوغ ولا يُخزَّن**، وعقد `DeleteAsync` بلا مُنفِّذ — والسابقة الموجودة (`UserRepository.DeleteAsync`) تحمل خطأً يجب عدم نسخه: تكتب `[DeletedBy] = @Id` أي هوية المحذوف لا هوية الفاعل (`UserRepository.cs:221`).
- **سابقة معمارية موثقة يجب مواجهتها**: `AssignmentRemovalSqlTests.cs` يوثق حادثة إنتاج سابقة (SQL 2627) سببها Soft Delete خلف قيد `UNIQUE` غير مفلتر على `UserRoles`، وحُلت بالعودة للحذف المادي. الفارق هنا: `CreateApplicationCommandHandler` يفحص `ExistsByCodeAsync` الشامل قبل الإدخال فيعيد `DuplicateCode` 409 قبل أي 2627 (يبقى 2627 ممكناً في سباق إنشاء/إنشاء فقط — مقبول وموثق).

### 2.5 ما صمد من الخطة أمام التفنيد (إنصافاً)

- **البذور آمنة تماماً ضد الإحياء** — تحقق مستقل: الكتابة الوحيدة على `Applications` في مسار النشر هي `INSERT` محروس بـId، بلا `UPDATE/MERGE`، والنسخة الثانية `01_DefaultApplications.sql` مصنفة `<None>` ولا تُنشر.
- **سطح OAuth مغطى مجاناً**: `Authorize` و`TokenExchange` و`PublicBranding` تمر عبر `GetByCodeAsync` + فحص `IsActive` — تغطية مزدوجة بعد الفلترة. لا يوجد Client Credentials Grant في الكود.
- **مسارات الإنشاء محمية بالوراثة**: `CreateApiKey`, `CreateWebhookKey`, `EnableApplication`, `AssignAppRole`, `GrantPermission`, `UpdateApplication` كلها تحمّل التطبيق بـ`GetByIdAsync` أولاً — تفشل طبيعياً بعد الفلترة.
- **نمط Soft Delete موجود مسبقاً على `Users`** بنفس الثلاثي والاصطلاح (`DF_Users_IsDeleted`، فهارس مفلترة `IsDeleted = 0`، قيود UNIQUE غير مفلترة) — الخطة متسقة مع سابقة حية (DRY).
- **اصطلاح تسمية قيود Default إلزامي**: `DF_<Table>_<Column>` مطبق على ~130 قيداً بلا استثناء، وخلفيته حادثة نشر موثقة (`RECONCILE_prod_constraint_names.sql:9-19`) — العمود الجديد يجب أن يُسمى `DF_Applications_IsDeleted`.

---

## الجزء الثالث: الخطة المنقحة والمحصنة

### 3.0 ملخص الفروقات عن الخطة الأصلية

| # | البند الأصلي | القرار المنقح | السبب |
|---|---|---|---|
| D1 | Soft Delete بالثلاثي `IsDeleted/DeletedAt/DeletedBy` | **إبقاء** | 15/17 FK بلا Cascade؛ الحذف المادي للتطبيق المزروع مستحيل |
| D2 | إزالة مفهوم تطبيق النظام | **إبقاء الإزالة + إضافة حماية بقائمة Id من الإعدادات** | الحارس المُزال كود ميت؛ الحماية الجديدة بـId لا بالاسم/الكود — متوافقة مع المبدأ الحاكم للخطة |
| D3 | منع الحذف عند وجود مفاتيح نشطة | **إبطال المفاتيح تعاقبياً داخل معاملة الحذف؛ إبقاء المنع على تعيينات المستخدمين والمنظمات فقط** | يعالج UX الحادثة (إبطال يدوي واحداً واحداً)؛ فحوصات المنع أثبتت نقصها البنيوي |
| D4 | إضافة `HasActiveWebhookKeysAsync` | **إلغاء** | يبتلعه D3 |
| D5 | تحصين Refresh Token | **إبقاء + إلزام الشحن في نفس الـCommit مع فلاتر المستودع** | ثغرة تصعيد الصلاحيات (2.1) |
| D6 | الاستعادة خارج النطاق | **تبقى خارج النطاق بشرطين تعويضيين**: حماية الـId المزروع افتراضياً + Runbook استعادة SQL موثق | البذور لا تُحيي؛ بدون التعويضين الحذف بابٌ أبدي |
| D7 | 19 ملفاً | **28 ملفاً** (21 كود + 7 ترجمة) | القسم 2.4 |
| D8 | لا Seam جديد | **إضافة `GetByIdIncludingDeletedAsync`** | إثراء التدقيق التاريخي |

### 3.1 قاعدة البيانات

**`Auth/Auth_DB/dbo/Tables/Core/Applications.sql`** — إضافة في نهاية قائمة الأعمدة، بمحاكاة `Users.sql:33-35` حرفياً:

```sql
[IsDeleted] BIT NOT NULL CONSTRAINT [DF_Applications_IsDeleted] DEFAULT 0,
[DeletedAt] DATETIME2 NULL,
[DeletedBy] UNIQUEIDENTIFIER NULL,
```

- التسمية `DF_Applications_IsDeleted` **إلزامية** (سابقة `BlockOnPossibleDataLoss`).
- `DeletedBy` بلا FK (اصطلاح الجدول: `CreatedBy/ModifiedBy` بلا FK).
- `UQ_Applications_Code` يبقى غير مفلتر (الكود محجوز للأبد) — سباق 2627 إنشاء/إنشاء مقبول وموثق.
- تعديل الفهرس المفلتر: `IX_Applications_Code ... WHERE [IsDeleted] = 0` (كان `IsActive = 1`؛ مسار `GetByCodeAsync` الساخن سيفلتر على `IsDeleted`).
- `IX_Applications_IsActive` يبقى كما هو.
- النشر إضافة عمود بـDefault — ليس عملية فقد بيانات؛ `BlockOnPossibleDataLoss` لا يعترضها.

**`Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql`** — فرع `ELSE` لبذرة التطبيق (هذا الملف، لا النسخة الميتة `01_DefaultApplications.sql`):

```sql
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [Id] = @AuthAppId AND [IsDeleted] = 1)
        PRINT 'WARNING: Auth System application exists but is soft-deleted; deployment will NOT resurrect it. See restore runbook.';
    ELSE
        PRINT 'Auth System application already exists';
END
```

### 3.2 طبقة Domain

**`Application.cs`**: خصائص `IsDeleted/DeletedAt/DeletedBy` بـ`private set`؛ سلوك `Delete(Guid deletedBy)` (يضبط الثلاثي + `IsActive = false` + `SetModified`)؛ مُرطِّب `LoadDeletionState(...)` على سابقة `LoadReauthenticationMaxAge` — **عدم المساس بالـConstructor ذي الـ17 وسيطاً** (كسره يكسر المستودع والاختبارات معاً). لا Domain Events — الـAggregate لا يستخدمها أصلاً.

**`IApplicationRepository.cs`**: `DeleteAsync(Guid id, Guid deletedBy, CancellationToken)` — تمرير **الفاعل** لا نسخ خطأ `UserRepository`؛ إضافة `GetByIdIncludingDeletedAsync`؛ حذف `HasActiveApiKeysAsync` (مستدعيه الوحيد يزول مع D3).

**`ApplicationErrors.cs`**: حذف `CannotDeleteSystemApplication` و`CannotModifySystemApplication` (صفر مستدعين) و`HasActiveApiKeys`؛ إضافة:

```csharp
public static Error ProtectedApplication => Error.Forbidden(
    code: "Application.Protected",
    description: "This application is protected by configuration and cannot be deleted.");
```

### 3.3 طبقة Infrastructure

**`ApplicationRepository.cs`**:
- `AND [IsDeleted] = 0` في خمسة مواضع: `GetByIdAsync`, `GetByCodeAsync`, `GetAllAsync`, `GetActiveAsync`, `GetPagedAsync` (تستبدل `WHERE 1=1`، وتشمل استعلامي العد والبيانات). قوائم الأعمدة تبقى كما هي حرفياً — لا "إصلاح" لانحراف `ReauthenticationMaxAgeMinutes` القائم ضمن هذه الدفعة.
- `ExistsByCodeAsync` بلا تغيير (شامل للمحذوف عمداً).
- `GetByIdIncludingDeletedAsync` جديد: أعمدة `GetByIdAsync` + الثلاثي، بلا فلتر، بلا استعلام Redirect URIs.
- `DeleteAsync` يُعاد كتابته **معاملة واحدة**:

```sql
UPDATE [dbo].[Applications]
SET [IsDeleted] = 1, [DeletedAt] = GETUTCDATE(), [DeletedBy] = @DeletedBy,
    [IsActive] = 0, [ModifiedAt] = GETUTCDATE(), [ModifiedBy] = @DeletedBy
WHERE [Id] = @Id AND [IsDeleted] = 0;

UPDATE [dbo].[ApiKeys]
SET [RevokedAt] = GETUTCDATE(), [RevokedBy] = @DeletedBy, [RevokeReason] = N'Application deleted'
WHERE [ApplicationId] = @Id AND [RevokedAt] IS NULL;

UPDATE [dbo].[WebhookKeys]
SET [RevokedAt] = GETUTCDATE(), [RevokedBy] = @DeletedBy, [RevokeReason] = N'Application deleted'
WHERE [ApplicationId] = @Id AND [RevokedAt] IS NULL;
```

(أعمدة `RevokedBy/RevokeReason` مؤكدة على الجدولين: `ApiKeys.sql:18-20`, `WebhookKeys.sql:15-17`.)

**`ApiKeyRepository.cs`** — تحصين استعلامي البحث كليهما (`GetByHashAsync` واستعلام المرشحين بالبادئة):

```sql
SELECT k.* FROM [dbo].[ApiKeys] k
INNER JOIN [dbo].[Applications] a ON a.[Id] = k.[ApplicationId]
WHERE ... AND a.[IsActive] = 1 AND a.[IsDeleted] = 0
```

**`WebhookKeyRepository.cs`** — الربط والشرطان نفسهما على `GetByHashAsync`.

**القراء الثانويون**: `DashboardStatsRepository.GetAppActivityAsync`، `RoleRepository.GetRoleApplicationsAsync`، `UserRepository.GetUserApplicationsAsync` — إضافة `a.[IsDeleted] = 0`. **يبقى عمداً بلا فلتر** (قرارات موثقة): ربط الأسماء التاريخي في `AuditLogRepository`, `NotificationTemplateRepository`, `NotificationOutboxRepository`, وعدّادات `DashboardStatsRepository`.

### 3.4 طبقة Application

**جديد `Auth.Application/Configuration/ApplicationProtectionSettings.cs`**:

```csharp
public class ApplicationProtectionSettings
{
    public const string SectionName = "ApplicationProtection";
    public List<Guid> ProtectedApplicationIds { get; set; } = [];
}
```

**`DeleteApplicationCommandHandler.cs`**:
- حذف السطر 34 — مع النص صراحة في وصف الـPR: "يزيل حارساً لم يحمِ التطبيق المزروع يوماً؛ البديل حماية بـId من الإعدادات".
- حقن `IOptions<ApplicationProtectionSettings>`؛ بعد فحص `NotFound`:

```csharp
if (_protection.ProtectedApplicationIds.Contains(request.Id))
    return ApplicationErrors.ProtectedApplication;   // 403 — لا مطابقة اسم أو كود
```

- حذف كتلة `HasActiveApiKeysAsync`؛ إبقاء فحصي المستخدمين والمنظمات (409).
- `DeleteAsync(request.Id, request.DeletedBy, ct)` — الفاعل يُخزَّن أخيراً لا يُسجَّل فقط.

**`RefreshTokenCommandHandler.cs`** — **في نفس الـCommit مع فلاتر المستودع، بلا استثناء**:

```csharp
string? audience = null;
if (storedToken.ApplicationId.HasValue)
{
    var application = await _applicationRepository.GetByIdAsync(
        storedToken.ApplicationId.Value, cancellationToken);
    if (application is null || !application.IsActive)
    {
        _logger.LogWarning("Refresh rejected: application {ApplicationId} deleted or inactive",
            storedToken.ApplicationId);
        return ApplicationErrors.ApplicationInactive;   // خطأ قائم ومترجم مسبقاً
    }
    audience = application.Code;   // السقوط إلى جمهور المنصة أُزيل
}
```

**Seam التدقيق** — تحويل `GetByIdAsync` إلى `GetByIdIncludingDeletedAsync` في: `NameLookupHelper.cs`، `GetAuditLogByIdQueryHandler`، `GetAuditLogsByUserQueryHandler`، `GetAuditLogsByEntityQueryHandler`، `ExportAuditLogsCommandHandler`.

`ValidateApiKeyQueryHandler` / `ValidateWebhookKeyQueryHandler`: **بلا تغيير** — التحصين في SQL المستودع (جولة واحدة، يغطي كل المستدعين).

### 3.5 API والإعدادات والترجمة

- `Program.cs`: تسجيل `ApplicationProtectionSettings` من قسم الإعدادات.
- `appsettings.json`:

```json
"ApplicationProtection": {
  "ProtectedApplicationIds": [ "00000000-0000-0000-0000-000000000001" ]
}
```

- الترجمة (7 ملفات `DomainErrors*.resx`): إضافة `Application.Protected` **إلزامية** (`DomainErrorResourceCoverageTests` يفشل عند الغياب). حذف مفاتيح النظام القديمة اختياري — المفاتيح الزائدة مسموحة في اختبارات التغطية؛ يُتحقق قبل الحذف.
- اختياري موصى به: ذراع `SqlException { Number: 547 }` → 409 في `ExceptionHandlingMiddleware` — دفاعٌ عن عمليات الحذف المادي **الأخرى** الباقية في الكود (Roles, Permissions, NotificationTemplates)؛ وإضافة `ProducesResponseType(409)` الناقصة على نقطة الحذف.

### 3.6 الاختبارات

- `ApplicationCommandHandlerTests.cs`: **حذف** `Handle_SystemApplication_ReturnsForbidden` (الاختبار الذي أخفى خطأ الإنتاج بتفبريك `code: "AUTH"`) واختبار تعارض المفاتيح؛ **إضافة**: حماية الـId المحمي (403 ولا حذف)، الحذف الصحيح يستدعي `DeleteAsync(appId, deletedBy, ct)`، تطبيق بمفاتيح مبطلة يُحذف بنجاح (Regression الحادثة)؛ إبقاء اختباري 409 للمستخدمين والمنظمات؛ تصحيح تواقيع الـMocks.
- `RefreshTokenCommandHandlerTests.cs` — **اختبار Regression لتصعيد الصلاحيات**: توكن مقيد بتطبيق + `GetByIdAsync` يعيد null ⇒ `ApplicationInactive` و`GenerateAccessToken` لا يُستدعى إطلاقاً؛ تطبيق معطل ⇒ كذلك؛ تطبيق نشط ⇒ `audience = app.Code`؛ `ApplicationId = null` ⇒ Audience فارغ (مسار أول-طرف بلا تغيير).
- جديد `Auth_API.Tests/Infrastructure/ApplicationSoftDeleteSqlTests.cs` على سابقة `AssignmentRemovalSqlTests`: لا `DELETE FROM [dbo].[Applications]` في المستودع؛ `[IsDeleted] = 0` في الاستعلامات الخمسة؛ ربط `Applications` بشرطي `IsActive/IsDeleted` في مستودعي المفاتيح. (الحل الوحيد المتاح — لا اختبارات تكامل بقاعدة بيانات في المشروع.)

### 3.7 ترتيب التنفيذ

1. **Commit 1 — قاعدة البيانات** (قابل للنشر أولاً؛ متوافق رجعياً: قوائم أعمدة الكود القديم الصريحة تتجاهل الأعمدة الجديدة).
2. **Commit 2 — كل تغيير الكود وحدة ذرية واحدة** (فلاتر المستودع + بوابة الـRefresh لا يُفصلان).
3. **Commit 3 — الاختبارات** (يجوز دمجه مع 2). اختياري 4: ذراع 547 والـAttribute.

### 3.8 Runbook تشغيلي (يُرفق بوصف الـPR)

- **جرد الإنتاج أولاً**: محاولة الحذف الشامل في الحادثة قد تكون أزالت مادياً تطبيقاتٍ بلا صفوف أبناء — والبذور لا تعيد إلا تطبيق `auth`. يُنفذ `SELECT [Id], [Code], [Name] FROM [dbo].[Applications]` ويُقارن بالمتوقع قبل النشر.
- **استعادة SQL (إلى أن يُشحن Endpoint الاستعادة)**: `UPDATE [dbo].[Applications] SET [IsDeleted]=0, [DeletedAt]=NULL, [DeletedBy]=NULL, [IsActive]=1, [ModifiedAt]=GETUTCDATE() WHERE [Id]=@Id;` — المفاتيح تبقى مبطلة ويُعاد إصدارها عمداً. `SET QUOTED_IDENTIFIER ON` إلزامي عبر sqlcmd (فهارس مفلترة).
- ترتيب النشر: dacpac أولاً ثم API. التراجع عن API آمن بعد نشر القاعدة؛ لا تراجع عن الأعمدة.

### 3.9 نوافذ الكشف المقبولة والموثقة

| النافذة | المدة | السبب |
|---|---|---|
| توكنات وصول صادرة لدى خدمات نازلة | ≤ ~16 دقيقة | عمر التوكن 15د + انحراف 60ث؛ التجديد مقطوع فوراً |
| مفتاح API في ذاكرة SDK | ≤ 60 ثانية | `ApiKeyCacheDuration` بلا Invalidation |
| مفتاح Webhook في ذاكرة SDK | ≤ 300 ثانية | `WebhookKeyCacheDuration` بلا Invalidation |
| قوالب إشعارات مخبأة | ≤ 15 دقيقة | `TemplateCache` |

توسيع `RevokedTokens` بمحور Application **مرفوض الآن**: الـBlacklist يعمل داخل Auth API فقط، والتوكنات ذات Audience تطبيقي لا تصله أصلاً — كلفة بلا مكسب. يُسمى الـSeam للمستقبل: `RevocationType = 4 (Application)`.

---

## معايير القبول (تعديلات على قائمة الخطة الأصلية)

كل معايير الخطة الأصلية تبقى، مع:

- [ ] حذف تطبيق يحمل مفاتيح **نشطة** ينجح ويبطلها ذرياً بـ`RevokeReason = 'Application deleted'` (بدلاً من 409).
- [ ] حذف تطبيق ضمن `ProtectedApplicationIds` يعيد 403 `Application.Protected`.
- [ ] Refresh Token لتطبيق محذوف/معطل يعيد `ApplicationInactive` **ولا يُستدعى** `GenerateAccessToken` — لا سقوط إلى جمهور المنصة.
- [ ] صفوف التدقيق التاريخية تعرض اسم التطبيق المحذوف (عبر `GetByIdIncludingDeletedAsync`).
- [ ] مفتاح API/Webhook لتطبيق **معطل** (وليس محذوباً فقط) يُرفض — إغلاق الثغرة القائمة.
- [ ] `DeletedBy` يُخزَّن بهوية الفاعل لا هوية السجل.
- [ ] مخرجات النشر تطبع تحذيراً إذا كان التطبيق المزروع محذوفاً منطقياً.
