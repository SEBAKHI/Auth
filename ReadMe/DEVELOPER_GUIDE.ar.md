<div dir="rtl">

# دليل المطور لنظام AuthSystem

دليل شامل للمطورين لإعداد وتكوين واستخدام نظام AuthSystem — منصة مصادقة وتفويض متعددة المستأجرين على مستوى المؤسسات.

---

## جدول المحتويات

1. [نظرة عامة على النظام](#1-نظرة-عامة-على-النظام)
2. [المتطلبات الأساسية](#2-المتطلبات-الأساسية)
3. [البدء](#3-البدء)
4. [التعمق في البنية المعمارية](#4-التعمق-في-البنية-المعمارية)
5. [مرجع واجهة برمجة التطبيقات](#5-مرجع-واجهة-برمجة-التطبيقات)
6. [سيناريوهات العمل الشائعة](#6-سيناريوهات-العمل-الشائعة)
7. [نظرة عامة على مخطط قاعدة البيانات](#7-نظرة-عامة-على-مخطط-قاعدة-البيانات)
8. [أفضل ممارسات الأمان](#8-أفضل-ممارسات-الأمان)
9. [الاختبارات](#9-الاختبارات)
10. [استكشاف الأخطاء وإصلاحها](#10-استكشاف-الأخطاء-وإصلاحها)
11. [مصفوفة الصلاحيات](#11-مصفوفة-الصلاحيات)

---

## 1. نظرة عامة على النظام

### 1.1 ما هو AuthSystem

AuthSystem هو منصة مصادقة وتفويض جاهزة للإنتاج مبنية على NET 10. يوفر إدارة هوية متعددة التطبيقات ومتعددة المؤسسات مع صلاحيات هرمية، والتحكم بالوصول المبني على الأدوار (RBAC)، والمصادقة الثنائية، وتسجيل الدخول عبر مزودين خارجيين (Google)، وإدارة مفاتيح API، وتتبع الجلسات، وتسجيل تدقيق شامل. مصمم كخدمة هوية مركزية يمكن لتطبيقات متعددة التكامل معها عبر واجهات REST API.

### 1.2 قدرات API في لمحة سريعة

يوفر النظام **أكثر من 87 نقطة نهاية REST API** عبر **12 Controller**، منظمة في المجالات الوظيفية التالية:

| الوصف | عدد النقاط | الميزة |
|---|---|---|
| <div dir="rtl">اكتشاف OpenID Connect، مفاتيح JWKS، المفتاح العام</div> | 3 | **Discovery (OIDC)** |
| <div dir="rtl">تسجيل الدخول، التسجيل، تسجيل الدخول الخارجي، تحديث/إبطال الرموز، إدارة كلمات المرور، التحقق من البريد، إدارة الجلسات</div> | 18 | **Authentication** |
| <div dir="rtl">إعداد TOTP، التفعيل، التعطيل</div> | 3 | **Two-Factor Auth** |
| <div dir="rtl">CRUD، تعيين الأدوار/الصلاحيات، القفل/الفتح، التفعيل/التعطيل، الملف الشخصي</div> | 16 | **Users** |
| <div dir="rtl">CRUD محددة النطاق للتطبيقات</div> | 5 | **Roles** |
| <div dir="rtl">CRUD مع التضمينات الهرمية ودعم أحرف البدل</div> | 8 | **Permissions** |
| <div dir="rtl">تسجيل التطبيقات المتعددة مع أدوار وصلاحيات لكل تطبيق</div> | 7 | **Applications** |
| <div dir="rtl">CRUD متعدد المستأجرين، إدارة الأعضاء، الدعوات، اشتراكات التطبيقات، أدوار/صلاحيات الأعضاء</div> | 17 | **Organizations** |
| <div dir="rtl">قبول دعوة المؤسسة</div> | 1 | **Invitations** |
| <div dir="rtl">إنشاء، عرض، إبطال، تدوير مع فترة سماح</div> | 4 | **API Keys** |
| <div dir="rtl">استعلام، تصفية، حسب المستخدم/الكيان، تصدير (CSV/JSON)</div> | 5 | **Audit Logs** |
| <div dir="rtl">حالة الأسرار، توليد واستيراد المفاتيح (BYOK)، إدارة الأسرار المخصصة</div> | 9 | **Secrets (Admin)** |

> راجع [القسم 5 — مرجع API](#5-مرجع-api) للتفاصيل الكاملة لنقاط النهاية مع أمثلة الطلبات والاستجابات.

### 1.3 مخطط البنية المعمارية

```
┌──────────┐       ┌─────────────────────┐       ┌──────────────────┐       ┌────────────┐
│          │       │    API_Gateway       │       │    Auth_API       │       │            │
│  العميل  │──────▶│  (YARP بروكسي)      │──────▶│  (REST API)      │──────▶│ SQL Server │
│          │       │  المنفذ: 5034/7159  │       │  المنفذ: 5100/5101│       │            │
└──────────┘       └─────────────────────┘       └──────────────────┘       └────────────┘
                   │ + X-Gateway-Token    │       │ + مصادقة JWT      │
                   │ + X-Forwarded-For    │       │ + تفويض الصلاحيات │
                   │ + X-Correlation-ID   │       │ + تخزين الأسرار   │
                   │ + تحديد المعدل       │       │ + سجل التدقيق     │
                   └─────────────────────┘       └──────────────────┘
```

### 1.4 هيكل الحل

```
Auth/
├── src/
│   ├── Services/
│   │   ├── Auth.Domain          — الكيانات، الواجهات، التعدادات، تعريفات الأخطاء
│   │   ├── Auth.Application     — أوامر/استعلامات CQRS، كائنات نقل البيانات، المدققات، الإعدادات
│   │   ├── Auth.Infrastructure  — مستودعات Dapper، JWT، Argon2id، تخزين الأسرار، مصادقة Google، TOTP، SMTP
│   │   └── Auth_API             — واجهة REST API على ASP.NET Core 10 (12 متحكم، 87+ نقطة نهاية)
│   ├── Shared/
│   │   ├── Auth.Shared          — عقود الإعدادات المشتركة وأساسيات تخزين الأسرار
│   │   └── Auth_Localization    — ملفات الموارد لـ 7 لغات (en، ar، tr، fr، zh، ur، fa)
│   ├── Gateway/
│   │   └── API_Gateway          — بروكسي عكسي YARP مع تحديد المعدل ورؤوس الأمان
│   ├── Sdk/
│   │   └── Auth.Sdk             — حزمة SDK تثبّتها تطبيقات .NET الأخرى للتحقق من الرموز/مفاتيح API
│   ├── Setup/
│   │   └── Auth_Setup           — أداة سطر أوامر تُشغَّل مرة واحدة لتوليد تجزئة كلمة مرور المسؤول
│   └── Database/
│       └── Auth_DB              — مشروع قاعدة بيانات SQL Server (26 جدول، إجراءات مخزنة)
├── Tests/
│   └── Auth_API.Tests           — xUnit، Moq، FluentAssertions
└── Auth.sln
```

### 1.5 حزمة التقنيات والمبررات

| التقنية | الغرض | لماذا هذه وليس غيرها |
|---|---|---|
| **.NET 10** | بيئة التشغيل والإطار | أحدث إصدار مع دعم OpenAPI الأصلي وتحسينات الأداء ودعم AOT |
| **Dapper** | الوصول لقاعدة البيانات (ORM مصغر) | تحكم كامل بـ SQL، دعم الإجراءات المخزنة، أداء متفوق مقارنة بـ Entity Framework لأحمال المصادقة كثيفة القراءة |
| **MediatR** | نمط CQRS | معالجات أوامر/استعلامات منفصلة، سلوكيات خط الأنابيب للاهتمامات المشتركة، قابلية اختبار ممتازة |
| **ErrorOr** | معالجة الأخطاء | نمط الاتحاد المميز يتجنب التحكم بالتدفق عبر الاستثناءات |
| **FluentValidation** | التحقق من المدخلات | قواعد تحقق تصريحية منفصلة عن منطق المجال |
| **RS256 JWT** | توقيع الرموز | المفاتيح غير المتماثلة تسمح للخدمات الخارجية بالتحقق من الرموز باستخدام المفتاح العام دون مشاركة المفتاح الخاص (على عكس HS256) |
| **Argon2id** | تجزئة كلمات المرور | موصى بها من OWASP 2024؛ خوارزمية كثيفة الذاكرة مقاومة لهجمات GPU/ASIC (أفضل من bcrypt/PBKDF2) |
| **تخزين الأسرار (PlainText / Certificate / DPAPI)** | تشفير الأسرار عند الراحة | وضع `StorageMode` قابل للتبديل: PlainText لبداية سريعة عبر المنصات، Certificate لتشفير محمول يصمد عند نقل الخادم (موصى به للاستضافة المشتركة)، DPAPI لتشفير Windows المرتبط بالجهاز — دون الحاجة لخزنة مفاتيح خارجية |
| **YARP** | بوابة API | بروكسي عكسي أصلي لـ .NET؛ يُكوَّن في appsettings.json؛ تكامل .NET متفوق مقارنة بـ NGINX/Ocelot |
| **Serilog** | تسجيل منظم | أحواض متعددة (وحدة تحكم، ملف)، مُثريات، إخراج JSON منظم |
| **SQL Server + SSDT** | قاعدة البيانات | نظام RDBMS على مستوى المؤسسات؛ SSDT يوفر مخطط محكوم بالإصدارات مع إجراءات مخزنة |
| **xUnit + Moq + FluentAssertions** | الاختبارات | أشهر حزمة اختبارات .NET |
| **Otp.NET** | TOTP للمصادقة الثنائية | تطبيق خفيف الوزن لـ RFC 6238 |
| **Google.Apis.Auth** | المصادقة الخارجية | مكتبة Google الرسمية للتحقق من رموز ID |
| **API Versioning** | إدارة الإصدارات | إصدار عبر URL (`/api/v1/`) لتطور واضح لواجهة API |

---

## 2. المتطلبات الأساسية

| المتطلب | التفاصيل |
|---|---|
| **.NET 10 SDK** | [تحميل](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **SQL Server** | إصدار Express أو Developer (LocalDB يعمل أيضاً للتطوير) |
| **نظام Windows** | مطلوب فقط لوضعَي تخزين الأسرار `Dpapi` و`Certificate`؛ الوضع الافتراضي `PlainText` يعمل عبر المنصات (Linux/cPanel) |
| **Postman** (اختياري) | المجموعة متاحة في `Auth_API/Postman/AuthSystem.postman_collection.json` |
| **Visual Studio 2022+** (اختياري) | لنشر مشروع قاعدة البيانات SSDT |

---

## 3. البدء

### 3.1 الاستنساخ والبناء

```bash
git clone <repository-url>
cd AuthSystem
dotnet build Auth/Auth.sln
```

### 3.2 إعداد قاعدة البيانات

**الخيار أ: نشر SSDT (عبر Visual Studio)**

1. افتح `Auth/Auth.sln` في Visual Studio
2. انقر بزر الماوس الأيمن على مشروع `Auth_DB` ← **Publish**
3. كوّن سلسلة الاتصال المستهدفة (مثل `.\SQLEXPRESS`)
4. انقر **Publish**

**الخيار ب: الإعداد اليدوي**

نفذ سكريبتات SQL من `Auth/Auth_DB/dbo/Tables/` بالترتيب التالي:

**الجداول الأساسية (8):**
- `Users`، `Applications`، `Roles`، `Permissions`
- `UserRoles`، `RolePermissions`، `UserPermissions`، `PermissionImplications`

**جداول المصادقة (5):**
- `RefreshTokens`، `UserSessions`، `LoginAttempts`
- `UserExternalLogins`، `ExternalAuthProviders`

**جداول المؤسسات (6):**
- `Organizations`، `OrganizationUsers`، `OrganizationInvitations`
- `OrganizationApplications`، `OrganizationUserRoles`، `OrganizationUserPermissions`

**جداول الأمان (7):**
- `ApiKeys`، `ApiKeyScopes`، `TwoFactorAuth`
- `AuditLogs`، `PasswordHistory`
- `EmailVerificationTokens`، `PasswordResetTokens`

ثم نفذ جميع الإجراءات المخزنة من `Auth/Auth_DB/dbo/StoredProcedures/`.

### 3.3 التشغيل الأول وتوليد الأسرار

يحتاج النظام إلى ثلاثة أسرار: **زوج مفاتيح RSA** (توقيع JWT)، و**مفتاح HMAC** (تجزئة رموز التحديث)، و**رمز البوابة** (المصادقة بين الخدمات). عند التشغيل الأول، وعندما يكون `AutoGenerateKeys` مضبوطاً على `true`، تُولَّد الثلاثة تلقائياً — لا تشغّل أي أمر لتوليد المفاتيح.

| السر | الغرض |
|---|---|
| **مفتاح RSA** (2048 بت) | توقيع رموز الوصول JWT (RS256) |
| **مفتاح HMAC** (32 بايت) | تجزئة رموز التحديث (HMAC-SHA256) |
| **رمز البوابة** (32 بايت) | المصادقة بين الخدمات (بين API Gateway وAuth_API) |

**مكان كتابة الأسرار يعتمد على `SecretManagement:StorageMode`:**

| الوضع | مكان المفاتيح | محمي بواسطة | يُستخدم عندما |
|---|---|---|---|
| **`PlainText`** (افتراضي) | `appsettings.Production.json` (قابل للقراءة) | صلاحيات الملف فقط | بداية سريعة؛ عبر المنصات (Linux/cPanel) |
| **`Certificate`** | `secrets.dpapi` مشفّر | شهادة X.509 تملكها (محمولة بين الخوادم) | الاستضافة المشتركة؛ خوادم قد تُنقل |
| **`Dpapi`** | `secrets.dpapi` مشفّر | Windows DPAPI، مرتبط بهذا الجهاز + الحساب | جهاز Windows تتحكم به بالكامل |

**مواقع التخزين (وضعا Certificate/Dpapi):**
- ملف الأسرار: `SecretManagement:SecretFilePath` (مثل `%LOCALAPPDATA%/AuthSystem/Secrets/secrets.dpapi`)
- حلقة مفاتيح حماية البيانات: `DataProtection:KeyPath`

**هام:** بمجرد التوليد، لا تُعاد توليد المفاتيح تلقائياً. اضبط `AutoGenerateKeys: false` بعد التشغيل الأول كي يفشل التطبيق بصوت عالٍ إذا فُقد سر، بدلاً من توليد مفاتيح جديدة بصمت (ما يُبطل كل رمز صادر ويُسجّل خروج الجميع). لتدوير المفاتيح أو توفير مفاتيحك الخاصة (BYOK)، استخدم واجهة إدارة الأسرار (القسم 5.12).

> **هل تنشر إلى الإنتاج؟** إعداد أوضاع التخزين، وبوابة API، وBYOK / ترحيل الخادم، وفلفل كلمة المرور (Pepper) وفحص كلمات المرور المخترقة — كلها موثّقة بالكامل في **[PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md)**.

### 3.4 مرجع الإعدادات

جميع الإعدادات في `Auth/Auth_API/appsettings.json`. فيما يلي كل قسم.

#### سلاسل الاتصال

```json
{
  "ConnectionStrings": {
    "AuthDb": "Server=.\\SQLEXPRESS;Database=AuthDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True"
  }
}
```

#### إعدادات JWT

```json
{
  "Jwt": {
    "Issuer": "https://auth.yourdomain.com",
    "Audience": "https://api.yourdomain.com",
    "AccessTokenLifetimeMinutes": 15,
    "RefreshTokenLifetimeDays": 7,
    "KeyId": "auth-key-1",
    "RotateRefreshTokens": true,
    "ClockSkewSeconds": 60
  }
}
```

| الحقل | الوصف |
|---|---|
| `Issuer` | مطالبة `iss` في JWT؛ يحدد مُصدر الرمز |
| `Audience` | مطالبة `aud` في JWT؛ المستلم المقصود للرمز |
| `AccessTokenLifetimeMinutes` | انتهاء صلاحية رمز الوصول (افتراضي: 15 دقيقة) |
| `RefreshTokenLifetimeDays` | انتهاء صلاحية رمز التحديث (افتراضي: 7 أيام) |
| `KeyId` | معرف المفتاح لنقطة نهاية JWKS |
| `RotateRefreshTokens` | عند `true`، يولّد التحديث رمز تحديث جديد (تدوير) |
| `ClockSkewSeconds` | التسامح مع فروقات الساعة بين الخوادم |

#### سياسة كلمات المرور

```json
{
  "Password": {
    "MinimumLength": 8,
    "RequireUppercase": true,
    "RequireLowercase": true,
    "RequireDigit": true,
    "RequireSpecialCharacter": true,
    "HistoryCount": 3,
    "ExpirationDays": 0,
    "MaxFailedAttempts": 5,
    "LockoutDurationMinutes": 15,
    "Argon2MemorySize": 19456,
    "Argon2Iterations": 2,
    "Argon2Parallelism": 1,
    "SaltSize": 16,
    "HashSize": 32,
    "Pepper": { "Enabled": false },
    "BreachedPasswordCheck": {
      "Enabled": false,
      "Mode": "Enforce",
      "FailOpen": true,
      "RejectThreshold": 1,
      "TimeoutMs": 2000
    }
  }
}
```

| الحقل | الوصف |
|---|---|
| `MinimumLength` | الحد الأدنى لطول كلمة المرور (افتراضي 8؛ OWASP توصي بـ 12+) |
| `HistoryCount` | عدد كلمات المرور السابقة لمنع إعادة الاستخدام |
| `ExpirationDays` | أيام حتى انتهاء صلاحية كلمة المرور (0 = لا تنتهي) |
| `MaxFailedAttempts` | محاولات تسجيل الدخول الفاشلة قبل القفل |
| `LockoutDurationMinutes` | مدة قفل الحساب بعد أقصى عدد من المحاولات الفاشلة |
| `Argon2MemorySize` | تكلفة الذاكرة بالكيلوبايت (19456 = ~19 ميجابايت، موصى بها من OWASP 2024) |
| `Argon2Iterations` | تكلفة الوقت (عدد التكرارات) |
| `Argon2Parallelism` | عدد الخيوط للتجزئة |
| `Pepper.Enabled` | مزج سر من جانب الخادم في كل تجزئة Argon2id (دفاع متعمّق ضد اختراق قاعدة البيانات وحدها). تُوفَّر مادة المفتاح تلقائياً في مخزن الأسرار النشط؛ **انسخها احتياطياً مثل مفاتيح JWT — فقدانها يقفل جميع المستخدمين المُفلفَلين نهائياً.** |
| `BreachedPasswordCheck` | رفض أو تحذير من كلمات المرور المخترقة المعروفة عبر واجهة HIBP Pwned Passwords (نطاق، k-anonymity، بلا مفتاح). `Mode`: `Enforce` يرفض، `Warn` يسمح لكن يعيد ترويسة `X-Password-Warning`. `FailOpen` يسمح بالتغيير إذا تعذّر الوصول إلى HIBP. |

> كلٌّ من `Pepper` و`BreachedPasswordCheck` **اختياري** (افتراضياً `false`)؛ وتجزئة Argon2id نفسها مفعّلة دائماً. راجع [PRODUCTION_DEPLOYMENT_GUIDE.md §F](PRODUCTION_DEPLOYMENT_GUIDE.md) لتفاصيل الترحيل والتدوير.

#### إعدادات البوابة

```json
{
  "Gateway": {
    "TokenHeaderName": "X-Gateway-Token",
    "ValidationEnabled": true,
    "ExemptPaths": [
      "/.well-known/",
      "/health",
      "/ready",
      "/swagger",
      "/openapi"
    ]
  }
}
```

| الحقل | الوصف |
|---|---|
| `ValidationEnabled` | عند `true`، يجب أن تتضمن جميع الطلبات X-Gateway-Token (معطل في التطوير) |
| `TokenHeaderName` | اسم الرأس لرمز مصادقة البوابة |
| `ExemptPaths` | المسارات التي تتجاوز التحقق من رمز البوابة |

#### إعدادات الجلسات

```json
{
  "Session": {
    "LifetimeHours": 24,
    "ExtensionHours": 12,
    "MaxConcurrentSessions": 5,
    "IdleTimeoutMinutes": 60
  }
}
```

#### إعدادات البريد الإلكتروني

```json
{
  "Email": {
    "Enabled": false,
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "UseSsl": true,
    "Username": "",
    "FromEmail": "noreply@yourdomain.com",
    "FromName": "AuthSystem",
    "OtpExpirationMinutes": 15,
    "MaxOtpAttemptsPerWindow": 3,
    "OtpWindowMinutes": 60
  }
}
```

> **ملاحظة:** أبقِ كلمة مرور SMTP خارج `appsettings.json` — عيّنها عبر متغير البيئة `Email__Password`، أو خزّنها في مخزن الأسرار المشفّر (وضعا Certificate/Dpapi).

#### المصادقة الخارجية

```json
{
  "ExternalAuth": {
    "Google": {
      "Enabled": true,
      "ClientId": "your-google-client-id.apps.googleusercontent.com"
    }
  }
}
```

> معرف عميل Google قيمة عامة (ليس سراً). سر عميل Google غير مطلوب للتحقق من رموز ID.

#### CORS

```json
{
  "Cors": {
    "AllowedOrigins": ["https://app.yourdomain.com"],
    "AllowCredentials": true
  }
}
```

> **الإنتاج:** الأصول الصريحة **مطلوبة**. أحرف البدل (`*`) غير مسموح بها.
> **التطوير:** `["*"]` مسموح به عبر `appsettings.Development.json`.

#### تحديد المعدل

```json
{
  "RateLimiting": {
    "General": {
      "PermitLimit": 100,
      "WindowSeconds": 60
    },
    "Login": {
      "PermitLimit": 5,
      "WindowSeconds": 60
    }
  }
}
```

#### إدارة الأسرار

```json
{
  "SecretManagement": {
    "StorageMode": "PlainText",
    "SecretFilePath": "",
    "PlainTextTargetFile": "appsettings.Production.json",
    "AutoGenerateKeys": false,
    "EnableAdminApi": false,
    "RequiredPermission": "secrets.manage"
  }
}
```

| الحقل | الوصف |
|---|---|
| `StorageMode` | `PlainText` (افتراضي) أو `Certificate` أو `Dpapi` — راجع القسم 3.3 |
| `SecretFilePath` | موقع ملف `secrets.dpapi` المشفّر (وضعا Certificate/Dpapi؛ فارغ = الافتراضي `%LOCALAPPDATA%/AuthSystem/Secrets/secrets.dpapi`) |
| `PlainTextTargetFile` | الملف الذي تُكتب فيه المفاتيح المولّدة في وضع PlainText |
| `AutoGenerateKeys` | توليد تلقائي لـ RSA وHMAC ورمز البوابة عند التشغيل الأول. اضبطه على `false` بعد الإعداد الأولي. |
| `EnableAdminApi` | تفعيل نقاط نهاية `/api/v1/admin/secrets` (افتراضي: false) |
| `RequiredPermission` | الصلاحية المطلوبة لاستدعاء واجهة إدارة الأسرار |

#### حماية البيانات

```json
{
  "DataProtection": {
    "KeyPath": "",
    "Certificate": {
      "PfxPath": "",
      "PasswordEnvironmentVariable": "AUTH_DP_CERT_PASSWORD",
      "AdditionalPfxPaths": []
    }
  }
}
```

| الحقل | الوصف |
|---|---|
| `KeyPath` | مكان تخزين حلقة مفاتيح حماية البيانات. فارغ يعني الافتراضي `%ProgramData%/AuthSystem/Keys`؛ على IIS / الاستضافة المشتركة اضبطه على مجلد قابل للكتابة **خارج جذر الويب العام** ووجّه Auth API وAPI Gateway إلى **نفس** المجلد ليتشاركا حلقة واحدة |
| `Certificate` | يُستخدم فقط في وضع التخزين `Certificate`. فضّل `PasswordEnvironmentVariable` (`AUTH_DP_CERT_PASSWORD`) على تخزين كلمة مرور `.pfx` في الملف |

#### Serilog

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "Logs/auth-api-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

### 3.5 الفروقات بين بيئة التطوير والإنتاج

تجاوزات **`appsettings.Development.json`**:

| الإعداد | قيمة التطوير |
|---|---|
| `ConnectionStrings.AuthDb` | `.\SQLEXPRESS` مع مصادقة Windows |
| `Jwt.Issuer` | `http://localhost:5100` |
| `Jwt.Audience` | `http://localhost:5000` |
| `Gateway.ValidationEnabled` | `false` |
| `Cors.AllowedOrigins` | `["*"]` |

### 3.6 تشغيل الـ API والبوابة

**تشغيل Auth API:**

```bash
dotnet run --project Auth/Auth_API
# يستمع على: http://localhost:5100, https://localhost:5101
```

**تشغيل بوابة API (اختياري للتطوير):**

```bash
dotnet run --project Auth/API_Gateway
# يستمع على: http://localhost:5034, https://localhost:7159
```

> في التطوير، يمكنك استدعاء Auth_API مباشرة (التحقق من رمز البوابة معطل). في الإنتاج، يجب أن تمر جميع الطلبات عبر بوابة API.

### 3.7 التحقق من الإعداد

```bash
# فحص الصحة
curl http://localhost:5100/health

# فحص الجاهزية (يشمل قاعدة البيانات)
curl http://localhost:5100/ready

# اكتشاف OIDC
curl http://localhost:5100/.well-known/openid-configuration
```

استجابة OIDC ناجحة تؤكد أن مفاتيح توقيع JWT محملة والـ API جاهز.

---

## 4. التعمق في البنية المعمارية

### 4.1 طبقات البنية النظيفة

```
┌─────────────────────────────────────────┐
│           Auth_API (الطبقة الخارجية)     │
│  المتحكمات، البرمجيات الوسيطة، التفويض   │
├─────────────────────────────────────────┤
│         Auth.Infrastructure             │
│  المستودعات، JWT، Argon2، DPAPI، SMTP   │
├─────────────────────────────────────────┤
│          Auth.Application               │
│  الأوامر، الاستعلامات، DTOs، المدققات    │
├─────────────────────────────────────────┤
│          Auth.Domain (النواة)            │
│  الكيانات، الواجهات، الأخطاء، التعدادات  │
└─────────────────────────────────────────┘
```

**قاعدة التبعية:** التبعيات تشير إلى الداخل فقط. المجال ليس له تبعيات خارجية. التطبيق يعتمد فقط على المجال. البنية التحتية تعتمد على المجال والتطبيق. طبقة API تعتمد على البنية التحتية (التي تجلب كل شيء بشكل متعدٍّ).

### 4.2 CQRS مع MediatR

كل نقطة نهاية API ترسل **أمراً** (كتابة) أو **استعلاماً** (قراءة) عبر MediatR.

**اصطلاح التسمية:**
- أمر: `LoginCommand` ← `LoginCommandHandler`
- استعلام: `GetUserByIdQuery` ← `GetUserByIdQueryHandler`

**تنظيم الملفات:**
```
Auth.Application/Features/
├── Authentication/
│   ├── Commands/Login/
│   │   ├── LoginCommand.cs
│   │   └── LoginCommandHandler.cs
│   ├── Commands/Register/
│   └── Queries/GetSessions/
├── UserManagement/
├── RoleManagement/
└── ...
```

كل معالج ينفذ `IRequestHandler<TRequest, ErrorOr<TResponse>>` ويستقبل التبعيات عبر حقن المنشئ.

### 4.3 معالجة الأخطاء (نمط ErrorOr)

المعالجات تعيد `ErrorOr<T>` بدلاً من رمي الاستثناءات. المتحكمات تربط النتائج باستجابات HTTP:

```
ErrorOr<T> نجاح  → 200/201 مع جسم الاستجابة
ErrorOr<T> خطأ   → يُربط بـ ProblemDetails (RFC 7807)
```

**ربط الأخطاء بـ HTTP:**

| نوع الخطأ | كود HTTP |
|---|---|
| `Error.Validation` | 400 طلب غير صالح |
| `Error.NotFound` | 404 غير موجود |
| `Error.Conflict` | 409 تعارض |
| `Error.Forbidden` | 403 محظور |
| `Error.Unauthorized` | 401 غير مصرح |
| افتراضي | 500 خطأ داخلي في الخادم |

**تنسيق استجابة ProblemDetails:**

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "User.InvalidCredentials",
  "status": 400,
  "detail": "بيانات الاعتماد المقدمة غير صالحة.",
  "instance": "/api/v1/auth/login",
  "correlationId": "abc-123"
}
```

### 4.4 التفويض المبني على الصلاحيات

يستخدم النظام نظام تفويض مبني على الصلاحيات مخصص (وليس أدوار ASP.NET Identity).

**كيف يعمل:**

1. خاصية `[RequirePermission("users:read")]` تُطبق على إجراء المتحكم
2. `PermissionPolicyProvider` ينشئ ديناميكياً سياسة تفويض للصلاحية
3. `PermissionRequirementHandler` يتحقق من مطالبات `permissions` في JWT مقابل المتطلب
4. مطابقة أحرف البدل مدعومة:
   - تامة: `users:read` تطابق `users:read`
   - بدل: `users:*` تطابق `users:read`، `users:create`، إلخ.
   - شاملة: `*` تطابق كل شيء

**تنسيق كود الصلاحية:** `{مورد}:{إجراء}` أو `{تطبيق}:{مورد}:{إجراء}`

أمثلة: `users:read`، `roles:create`، `crm:leads:read`، `org:members:manage`

### 4.5 خط أنابيب البرمجيات الوسيطة

الطلبات تمر عبر البرمجيات الوسيطة بهذا الترتيب:

```
الطلب
  │
  ▼
SecurityHeadersMiddleware        — يضيف رؤوس أمان OWASP، يزيل رأس Server
  │
  ▼
ExceptionHandlingMiddleware      — التقاط الاستثناءات العام → ProblemDetails
  │
  ▼
GatewayTokenValidationMiddleware — يتحقق من X-Gateway-Token (الإنتاج فقط)
  │
  ▼
Serilog Request Logging          — تسجيل HTTP منظم للطلبات/الاستجابات
  │
  ▼
Rate Limiting                    — محدد معدل بنافذة ثابتة
  │
  ▼
JwtBlacklistValidationMiddleware — يتحقق من الرمز مقابل قائمة الإبطال السوداء
  │
  ▼
JWT Authentication               — يتحقق من رمز Bearer، يضبط ClaimsPrincipal
  │
  ▼
Authorization                    — التحكم بالوصول المبني على الصلاحيات
  │
  ▼
إجراء المتحكم
```

### 4.6 إدارة الأسرار

تُحمَّل الأسرار عند بدء التشغيل وتُحقن في `IConfiguration`، فيقرأها بقية التطبيق كأي إعداد آخر. يحدد `SecretManagement:StorageMode` كيفية حمايتها عند الراحة:

| الوضع | التخزين | محمي بواسطة | محمول |
|---|---|---|---|
| **PlainText** (افتراضي) | `appsettings.Production.json` | صلاحيات الملف | ✅ انسخ الملف؛ عبر المنصات |
| **Certificate** | `secrets.dpapi` مشفّر | شهادة X.509 تملكها | ✅ احمل `.pfx` + حلقة المفاتيح + الملف |
| **Dpapi** | `secrets.dpapi` مشفّر | Windows DPAPI (هذا الجهاز) | ❌ مرتبط بالجهاز |

```
تدفق التشغيل (Certificate / Dpapi):
1. تُهيأ حلقة مفاتيح حماية البيانات في DataProtection:KeyPath
2. يحمّل مصدر الأسرار ملف secrets.dpapi (AddDpapiSecrets)
3. إذا كان مفتاح مفقوداً AND AutoGenerateKeys=true → يولّد RSA، HMAC، رمز البوابة
4. تُفك الأسرار وتُحقن في IConfiguration
5. JwtTokenService، RefreshTokenKeyService، GatewayMiddleware تقرأ من IConfiguration
```

> في وضع PlainText تُولَّد المفاتيح نفسها، لكنها تُكتب في `appsettings.Production.json` بدلاً من الملف المشفّر.

**ربط السر بمفتاح الإعداد (نفسه في كل الأوضاع):**
- `Jwt:PrivateKeyPem` — مفتاح RSA الخاص (PEM)
- `Jwt:PublicKeyPem` — مفتاح RSA العام (PEM، نص عادي لـ JWKS)
- `Jwt:RefreshTokenHmacKeyPlain` — مفتاح HMAC-SHA256 لتجزئة رموز التحديث
- `Gateway:ExpectedToken` (Auth API) / `Gateway:Token` (API Gateway) — سر البوابة المشترك
- `Email:Password` — كلمة مرور مصادقة SMTP
- `ConnectionStrings:AuthDb` — سلسلة اتصال قاعدة البيانات (اختياري؛ يمكن تشفيرها)
- `Password:Pepper:*` — مادة مفتاح الفلفل (عند تفعيل الفلفلة)
- `Custom:*` — أسرار مخصصة يحددها المستخدم

### 4.7 دورة حياة رمز JWT

```
تسجيل الدخول
  │
  ├──▶ رمز الوصول (RS256، 15 دقيقة)
  │     المطالبات: sub، email، name، roles[]، permissions[]، jti، iat، exp
  │
  └──▶ رمز التحديث (عشوائي 64 بايت، 7 أيام)
        يُخزن كتجزئة HMAC-SHA256 في قاعدة البيانات

التحديث
  │
  ├──▶ رمز وصول جديد
  └──▶ رمز تحديث جديد (القديم يُبطل — تدوير)

تسجيل الخروج
  │
  ├──▶ JTI رمز الوصول يُضاف إلى القائمة السوداء
  └──▶ رمز التحديث يُبطل في قاعدة البيانات
```

**التحقق الخارجي:** أي خدمة يمكنها التحقق من رموز الوصول باستخدام:
- `GET /.well-known/jwks.json` — مجموعة مفاتيح الويب JSON
- `GET /.well-known/public-key.pem` — مفتاح عام PEM

### 4.8 بوابة API (YARP)

بوابة API توفر نقطة دخول واحدة مع:

| الميزة | الإعداد |
|---|---|
| **التوجيه** | مبني على المسار: `/api/v1/auth/**`، `/api/v1/users/**`، إلخ. |
| **تحديد المعدل** | عام: 1000/60 ثانية، مصادقة: 20/60 ثانية، API: 100/60 ثانية |
| **حقن الرؤوس** | X-Gateway-Token، X-Forwarded-For/Host/Proto، X-Correlation-ID |
| **مراقبة الصحة** | فحوصات صحية نشطة على Auth_API كل 30 ثانية |
| **رؤوس الأمان** | نفس رؤوس OWASP كـ Auth_API |

**مسارات YARP مكوّنة في** `API_Gateway/appsettings.json` تحت `ReverseProxy.Routes`.

### 4.9 التوطين

النظام يدعم 7 لغات عبر ملفات موارد مضمنة:

| الكود | اللغة |
|---|---|
| `en` | الإنجليزية (افتراضي) |
| `ar` | العربية |
| `tr` | التركية |
| `fr` | الفرنسية |
| `zh` | الصينية |
| `ur` | الأردية |
| `fa` | الفارسية |

المستخدمون يضبطون لغتهم المفضلة عبر حقل `preferredLanguage`. رسائل الأخطاء والإشعارات تُعاد بلغة المستخدم.

---

## 5. مرجع API

**الرابط الأساسي:** `http://localhost:5100` (مباشر) أو `http://localhost:5034` (عبر البوابة)

**المصادقة:** معظم نقاط النهاية تتطلب رأس `Authorization: Bearer <access_token>`.

**إصدار API:** جميع نقاط النهاية المُصدَّرة تستخدم بادئة `/api/v1/`.

**أكواد الاستجابة الشائعة عبر جميع نقاط النهاية:**

| المعنى | الكود |
|---|---|
| <div dir="rtl">نجاح</div> | 200 |
| <div dir="rtl">تم الإنشاء</div> | 201 |
| <div dir="rtl">بلا محتوى (نجاح، بدون جسم)</div> | 204 |
| <div dir="rtl">طلب غير صالح (خطأ تحقق)</div> | 400 |
| <div dir="rtl">غير مصرح (رمز مفقود/غير صالح)</div> | 401 |
| <div dir="rtl">محظور (صلاحيات غير كافية)</div> | 403 |
| <div dir="rtl">غير موجود</div> | 404 |
| <div dir="rtl">تعارض (مكرر)</div> | 409 |
| <div dir="rtl">طلبات كثيرة جداً (تحديد المعدل)</div> | 429 |
| <div dir="rtl">خطأ داخلي في الخادم</div> | 500 |

### 5.0 فهرس نقاط النهاية

#### Discovery (OIDC) — 3 نقاط نهاية

| المصادقة | Endpoint | Method |
|---|---|---|
| <div dir="rtl">مجهول</div> | `/.well-known/openid-configuration` | GET |
| <div dir="rtl">مجهول</div> | `/.well-known/jwks.json` | GET |
| <div dir="rtl">مجهول</div> | `/.well-known/public-key.pem` | GET |

#### Authentication — 18 نقطة نهاية

| محدد المعدل | المصادقة | Endpoint | Method |
|---|---|---|---|
| <div dir="rtl">نعم</div> | <div dir="rtl">مجهول</div> | `/api/v1/auth/login` | POST |
| <div dir="rtl">نعم</div> | <div dir="rtl">مجهول</div> | `/api/v1/auth/register` | POST |
| — | <div dir="rtl">مجهول</div> | `/api/v1/auth/external-providers` | GET |
| <div dir="rtl">نعم</div> | <div dir="rtl">مجهول</div> | `/api/v1/auth/external-login` | POST |
| — | <div dir="rtl">مجهول</div> | `/api/v1/auth/refresh` | POST |
| — | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/logout` | POST |
| — | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/change-password` | POST |
| <div dir="rtl">نعم</div> | <div dir="rtl">مجهول</div> | `/api/v1/auth/forgot-password` | POST |
| — | <div dir="rtl">مجهول</div> | `/api/v1/auth/reset-password` | POST |
| — | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/sessions` | GET |
| — | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/sessions/{sessionId}` | DELETE |
| — | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/sessions` | DELETE |
| — | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/me` | GET |
| — | <div dir="rtl">مجهول/مصادق عليه</div> | `/api/v1/auth/revoke` | POST |
| — | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/introspect` | POST |
| <div dir="rtl">نعم</div> | <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/send-verification-email` | POST |
| <div dir="rtl">نعم</div> | <div dir="rtl">مجهول</div> | `/api/v1/auth/verify-email` | POST |
| <div dir="rtl">نعم</div> | <div dir="rtl">مجهول</div> | `/api/v1/auth/resend-verification-email` | POST |

#### Two-Factor Authentication — 3 نقاط نهاية

| المصادقة | Endpoint | Method |
|---|---|---|
| <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/2fa/setup` | POST |
| <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/2fa/enable` | POST |
| <div dir="rtl">مصادق عليه</div> | `/api/v1/auth/2fa/disable` | POST |

#### Users — 16 نقطة نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| `users:read` | `/api/v1/users` | GET |
| `users:read` | `/api/v1/users/{id}` | GET |
| `users:create` | `/api/v1/users` | POST |
| `users:update` | `/api/v1/users/{id}` | PUT |
| `users:delete` | `/api/v1/users/{id}` | DELETE |
| `users:manage-roles` | `/api/v1/users/{id}/roles` | POST |
| `users:read` | `/api/v1/users/{id}/roles` | GET |
| `users:manage-roles` | `/api/v1/users/{id}/roles/{roleId}` | DELETE |
| `users:read` | `/api/v1/users/{id}/permissions` | GET |
| `users:manage-permissions` | `/api/v1/users/{id}/permissions` | POST |
| `users:manage-permissions` | `/api/v1/users/{id}/permissions/{permissionId}` | DELETE |
| `users:manage` | `/api/v1/users/{id}/lock` | POST |
| `users:manage` | `/api/v1/users/{id}/unlock` | POST |
| `users:manage` | `/api/v1/users/{id}/activate` | POST |
| `users:manage` | `/api/v1/users/{id}/deactivate` | POST |
| <div dir="rtl">مصادق عليه</div> | `/api/v1/users/me` | GET |
| <div dir="rtl">مصادق عليه</div> | `/api/v1/users/me` | PUT |

#### Roles — 5 نقاط نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| `roles:read` | `/api/v1/roles` | GET |
| `roles:read` | `/api/v1/roles/{id}` | GET |
| `roles:create` | `/api/v1/roles` | POST |
| `roles:update` | `/api/v1/roles/{id}` | PUT |
| `roles:delete` | `/api/v1/roles/{id}` | DELETE |

#### Permissions — 8 نقاط نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| `permissions:read` | `/api/v1/permissions` | GET |
| `permissions:read` | `/api/v1/permissions/{id}` | GET |
| `permissions:create` | `/api/v1/permissions` | POST |
| `permissions:update` | `/api/v1/permissions/{id}` | PUT |
| `permissions:delete` | `/api/v1/permissions/{id}` | DELETE |
| `permissions:read` | `/api/v1/permissions/{id}/implications` | GET |
| `permissions:manage` | `/api/v1/permissions/{id}/implications` | POST |
| `permissions:manage` | `/api/v1/permissions/{id}/implications/{impliedId}` | DELETE |

#### Applications — 7 نقاط نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| `applications:read` | `/api/v1/applications` | GET |
| `applications:read` | `/api/v1/applications/{id}` | GET |
| `applications:read` | `/api/v1/applications/{id}/roles` | GET |
| `applications:read` | `/api/v1/applications/{id}/permissions` | GET |
| `applications:create` | `/api/v1/applications` | POST |
| `applications:update` | `/api/v1/applications/{id}` | PUT |
| `applications:delete` | `/api/v1/applications/{id}` | DELETE |

#### Organizations — 17 نقطة نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| <div dir="rtl">مصادق عليه</div> | `/api/v1/organizations` | GET |
| <div dir="rtl">مصادق عليه</div> | `/api/v1/organizations/{id}` | GET |
| <div dir="rtl">مصادق عليه</div> | `/api/v1/organizations` | POST |
| `org:update` | `/api/v1/organizations/{id}` | PUT |
| <div dir="rtl">المالك</div> | `/api/v1/organizations/{id}` | DELETE |
| `org:members:read` | `/api/v1/organizations/{id}/members` | GET |
| `org:members:manage` | `/api/v1/organizations/{orgId}/members/{userId}/role` | PUT |
| `org:members:manage` | `/api/v1/organizations/{orgId}/members/{userId}` | DELETE |
| `org:members:read` | `/api/v1/organizations/{id}/invitations` | GET |
| `org:members:invite` | `/api/v1/organizations/{id}/invitations` | POST |
| `org:apps:read` | `/api/v1/organizations/{id}/applications` | GET |
| `org:apps:manage` | `/api/v1/organizations/{id}/applications` | POST |
| `org:apps:manage` | `/api/v1/organizations/{id}/applications/{applicationId}` | PUT |
| `org:apps:manage` | `/api/v1/organizations/{id}/applications/{applicationId}` | DELETE |
| `org:permissions:manage` | `/api/v1/organizations/{orgId}/members/{userId}/roles` | POST |
| `org:permissions:manage` | `/api/v1/organizations/{orgId}/members/{userId}/permissions` | POST |

#### Invitations — نقطة نهاية واحدة

| المصادقة | Endpoint | Method |
|---|---|---|
| <div dir="rtl">مصادق عليه</div> | `/api/v1/invitations/{token}/accept` | POST |

#### API Keys — 4 نقاط نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| `apikeys:read` | `/api/v1/apikeys` | GET |
| `apikeys:create` | `/api/v1/apikeys` | POST |
| `apikeys:revoke` | `/api/v1/apikeys/{id}/revoke` | POST |
| `apikeys:rotate` | `/api/v1/apikeys/{id}/rotate` | POST |

#### Audit Logs — 5 نقاط نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| `auditlogs:read` | `/api/v1/audit-logs` | GET |
| `auditlogs:read` | `/api/v1/audit-logs/{id}` | GET |
| `auditlogs:read` | `/api/v1/audit-logs/users/{userId}` | GET |
| `auditlogs:read` | `/api/v1/audit-logs/entities/{entityType}/{entityId}` | GET |
| `auditlogs:export` | `/api/v1/audit-logs/export` | POST |

#### Secrets (Admin) — 9 نقاط نهاية

| الصلاحية | Endpoint | Method |
|---|---|---|
| `secrets.manage` | `/api/v1/admin/secrets/status` | GET |
| `secrets.manage` | `/api/v1/admin/secrets/generate/rsa` | POST |
| `secrets.manage` | `/api/v1/admin/secrets/generate/hmac` | POST |
| `secrets.manage` | `/api/v1/admin/secrets/generate/gateway-token` | POST |
| `secrets.manage` | `/api/v1/admin/secrets/import/rsa` | POST |
| `secrets.manage` | `/api/v1/admin/secrets/import/hmac` | POST |
| `secrets.manage` | `/api/v1/admin/secrets/import/gateway-token` | POST |
| `secrets.manage` | `/api/v1/admin/secrets/custom/{key}` | PUT |
| `secrets.manage` | `/api/v1/admin/secrets/custom/{key}` | DELETE |

---

### 5.1 الاكتشاف (OIDC)

هذه النقاط تتبع مواصفات اكتشاف OpenID Connect. هي **محايدة الإصدار** (بدون بادئة `/api/v1/`) و**مجهولة**.

#### GET `/.well-known/openid-configuration`

يعيد وثيقة اكتشاف OpenID Connect.

**المصادقة:** مجهول

**الاستجابة:**

```json
{
  "issuer": "http://localhost:5100",
  "jwks_uri": "http://localhost:5100/.well-known/jwks.json",
  "token_endpoint": "http://localhost:5100/api/v1/auth/login",
  "userinfo_endpoint": "http://localhost:5100/api/v1/auth/me",
  "end_session_endpoint": "http://localhost:5100/api/v1/auth/logout",
  "revocation_endpoint": "http://localhost:5100/api/v1/auth/revoke",
  "introspection_endpoint": "http://localhost:5100/api/v1/auth/introspect",
  "response_types_supported": [],
  "subject_types_supported": ["public"],
  "token_endpoint_auth_methods_supported": ["none"],
  "claims_supported": ["sub", "email", "name", "roles", "permissions", "iat", "exp", "aud", "iss"],
  "grant_types_supported": ["password", "refresh_token"]
}
```

> <div dir="rtl">الوثيقة تعلن القدرات المنفَّذة فقط. ستُضاف <code>authorization_endpoint</code> وقيم <code>response_types_supported</code> و<code>scopes_supported</code> و<code>code_challenge_methods_supported</code> (PKCE) مع تنفيذ تدفق authorization-code.</div>

#### GET `/.well-known/jwks.json`

يعيد مجموعة مفاتيح الويب JSON للتحقق الخارجي من الرموز.

**المصادقة:** مجهول

**الاستجابة:**

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "auth-key-1",
      "alg": "RS256",
      "n": "<modulus>",
      "e": "AQAB"
    }
  ]
}
```

#### GET `/.well-known/public-key.pem`

يعيد المفتاح العام RSA بتنسيق PEM.

**المصادقة:** مجهول

**الاستجابة:** `text/plain`

```
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...
-----END PUBLIC KEY-----
```

---

### 5.2 المصادقة

**المسار الأساسي:** `/api/v1/auth`

#### POST `/api/v1/auth/login`

مصادقة مستخدم بالبريد الإلكتروني وكلمة المرور.

**المصادقة:** مجهول | **محدد المعدل:** سياسة `login` (5 طلبات/60 ثانية)

**الطلب:**

```json
{
  "email": "user@example.com",
  "password": "SecureP@ssw0rd!",
  "deviceId": "optional-device-identifier"
}
```

**الاستجابة (200):**

```json
{
  "token": {
    "accessToken": "eyJhbGciOiJSUzI1NiIs...",
    "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
    "tokenType": "Bearer",
    "expiresIn": 900,
    "refreshExpiresIn": 604800
  },
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "displayName": "John Doe"
  },
  "requiresPasswordChange": false,
  "requiresTwoFactor": false
}
```

**أكواد الخطأ:** `User.InvalidCredentials`، `User.AccountLocked`، `User.AccountInactive`، `User.AccountPending`

#### POST `/api/v1/auth/register`

التسجيل الذاتي للمستخدمين الجدد.

**المصادقة:** مجهول | **محدد المعدل:** سياسة `login`

**الطلب:**

```json
{
  "email": "newuser@example.com",
  "password": "SecureP@ssw0rd!",
  "firstName": "Jane",
  "lastName": "Doe",
  "displayName": "Jane Doe",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "UTC",
  "createOrganization": false
}
```

| الحقل | مطلوب | الوصف |
|---|---|---|
| `email` | نعم | يجب أن يكون فريداً عبر جميع المستخدمين |
| `password` | نعم | يجب أن يستوفي متطلبات سياسة كلمة المرور |
| `firstName` | نعم | الاسم الأول للمستخدم |
| `lastName` | نعم | اسم العائلة للمستخدم |
| `createOrganization` | لا | إذا `true`، ينشئ مؤسسة شخصية للمستخدم (افتراضي: `false`) |

**الاستجابة (201):**

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "maskedEmail": "new***@example.com",
  "message": "تم التسجيل بنجاح. يرجى التحقق من بريدك الإلكتروني.",
  "organizationCreated": false
}
```

#### GET `/api/v1/auth/external-providers`

عرض قائمة مزودي المصادقة الخارجيين المفعلين.

**المصادقة:** مجهول

**الاستجابة (200):**

```json
[
  {
    "code": "google",
    "name": "Google",
    "iconUrl": "https://...",
    "isEnabled": true,
    "displayOrder": 1
  }
]
```

#### POST `/api/v1/auth/external-login`

المصادقة عبر مزود خارجي (مثل Google).

**المصادقة:** مجهول | **محدد المعدل:** سياسة `login`

**الطلب:**

```json
{
  "provider": "google",
  "idToken": "eyJhbGciOiJSUzI1NiIs...",
  "nonce": "random-nonce-value",
  "createOrganization": false
}
```

**الاستجابة (200):** نفس استجابة تسجيل الدخول.

> النظام يتحقق من رمز Google ID من جانب الخادم، ينشئ/يربط حساب المستخدم، ويعيد رموز JWT.

#### POST `/api/v1/auth/refresh`

استبدال رمز التحديث برموز وصول وتحديث جديدة.

**المصادقة:** مجهول

**الطلب:**

```json
{
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4..."
}
```

**الاستجابة (200):**

```json
{
  "accessToken": "eyJhbGciOiJSUzI1NiIs...",
  "refreshToken": "bmV3IHJlZnJlc2ggdG9rZW4...",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "refreshExpiresIn": 604800
}
```

> عندما يكون `RotateRefreshTokens` مضبوطاً على `true` (افتراضي)، يُبطل رمز التحديث القديم ويُصدر رمز جديد.

#### POST `/api/v1/auth/logout`

إنهاء الجلسة الحالية وإبطال الرموز.

**المصادقة:** مصادق عليه

**الطلب:**

```json
{
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "logoutAllDevices": false
}
```

| الحقل | الوصف |
|---|---|
| `refreshToken` | اختياري؛ إذا قُدم، يُبطل رمز التحديث المحدد |
| `logoutAllDevices` | إذا `true`، يُبطل جميع الجلسات والرموز للمستخدم |

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/auth/change-password`

تغيير كلمة مرور المستخدم المصادق عليه.

**المصادقة:** مصادق عليه

**الطلب:**

```json
{
  "currentPassword": "OldP@ssw0rd!",
  "newPassword": "NewSecureP@ss1!",
  "confirmNewPassword": "NewSecureP@ss1!",
  "terminateSessions": false
}
```

| الحقل | الوصف |
|---|---|
| `terminateSessions` | إذا `true`، ينهي جميع الجلسات الأخرى بعد تغيير كلمة المرور |

**الاستجابة:** 204 بلا محتوى

**التحققات:** فرض سياسة كلمة المرور، فحص سجل كلمات المرور (آخر 3 كلمات مرور).

#### POST `/api/v1/auth/forgot-password`

بدء عملية إعادة تعيين كلمة المرور (يرسل بريداً إلكترونياً برمز إعادة التعيين/OTP).

**المصادقة:** مجهول | **محدد المعدل:** سياسة `login`

**الطلب:**

```json
{
  "email": "user@example.com"
}
```

**الاستجابة (200):**

```json
{
  "message": "إذا كان البريد الإلكتروني موجوداً، فقد تم إرسال رابط إعادة تعيين كلمة المرور.",
  "maskedEmail": "us***@example.com"
}
```

> الاستجابة غامضة عمداً لمنع تعداد البريد الإلكتروني.

#### POST `/api/v1/auth/reset-password`

إكمال إعادة تعيين كلمة المرور باستخدام الرمز المستلم عبر البريد الإلكتروني.

**المصادقة:** مجهول

**الطلب:**

```json
{
  "email": "user@example.com",
  "token": "reset-token-from-email",
  "newPassword": "NewSecureP@ss1!",
  "confirmNewPassword": "NewSecureP@ss1!",
  "terminateSessions": true
}
```

**الاستجابة:** 204 بلا محتوى

#### GET `/api/v1/auth/sessions`

عرض جميع الجلسات النشطة للمستخدم المصادق عليه.

**المصادقة:** مصادق عليه

**الاستجابة (200):**

```json
[
  {
    "id": "3fa85f64-...",
    "ipAddress": "192.168.1.1",
    "userAgent": "Mozilla/5.0...",
    "deviceId": "device-123",
    "createdAt": "2026-03-12T10:00:00Z",
    "lastActivityAt": "2026-03-12T14:30:00Z",
    "expiresAt": "2026-03-13T10:00:00Z",
    "isCurrent": true
  }
]
```

#### DELETE `/api/v1/auth/sessions/{sessionId}`

إنهاء جلسة محددة.

**المصادقة:** مصادق عليه

**الاستجابة:** 204 بلا محتوى

#### DELETE `/api/v1/auth/sessions`

إنهاء جميع الجلسات باستثناء الحالية.

**المصادقة:** مصادق عليه

**الاستجابة (200):**

```json
{
  "terminatedCount": 3
}
```

#### GET `/api/v1/auth/me`

الحصول على ملف المستخدم المصادق عليه مع الأدوار والصلاحيات.

**المصادقة:** مصادق عليه

**الاستجابة (200):**

```json
{
  "id": "3fa85f64-...",
  "email": "user@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "displayName": "John Doe",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "UTC",
  "emailConfirmed": true,
  "twoFactorEnabled": false,
  "roles": ["admin", "user"],
  "permissions": ["users:read", "users:create", "roles:read"]
}
```

#### POST `/api/v1/auth/revoke`

إبطال رمز (متوافق مع RFC 7009).

**المصادقة:** مجهول (إبطال ذاتي للرمز) أو مصادق عليه

**الطلب:**

```json
{
  "token": "token-to-revoke",
  "tokenTypeHint": "access_token"
}
```

| قيم `tokenTypeHint` | الوصف |
|---|---|
| `access_token` | يُبطل رمز الوصول (يضيف JTI إلى القائمة السوداء) |
| `refresh_token` | يُبطل رمز التحديث |

**الاستجابة:** 200 نجاح

#### POST `/api/v1/auth/introspect`

فحص صلاحية رمز ومطالباته (متوافق مع RFC 7662).

**المصادقة:** مصادق عليه

**الطلب:**

```json
{
  "token": "token-to-inspect",
  "tokenTypeHint": "access_token"
}
```

**الاستجابة (200):**

```json
{
  "active": true,
  "sub": "3fa85f64-...",
  "email": "user@example.com",
  "exp": 1710244200,
  "iat": 1710243300,
  "iss": "http://localhost:5100",
  "aud": "http://localhost:5000",
  "tokenType": "access_token"
}
```

#### POST `/api/v1/auth/send-verification-email`

إرسال بريد التحقق للمستخدم المصادق عليه.

**المصادقة:** مصادق عليه | **محدد المعدل:** سياسة `login`

**الاستجابة (200):**

```json
{
  "expiresAt": "2026-03-12T10:15:00Z",
  "maskedEmail": "us***@example.com"
}
```

#### POST `/api/v1/auth/verify-email`

التحقق من البريد الإلكتروني باستخدام رمز OTP المستلم عبر البريد.

**المصادقة:** مجهول | **محدد المعدل:** سياسة `login`

**الطلب:**

```json
{
  "userId": "3fa85f64-...",
  "otp": "123456"
}
```

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/auth/resend-verification-email`

إعادة إرسال التحقق من البريد الإلكتروني لعنوان بريد محدد.

**المصادقة:** مجهول | **محدد المعدل:** سياسة `login`

**الطلب:**

```json
{
  "email": "user@example.com"
}
```

**الاستجابة (200):**

```json
{
  "expiresAt": "2026-03-12T10:15:00Z",
  "maskedEmail": "us***@example.com"
}
```

---

### 5.3 المصادقة الثنائية

**المسار الأساسي:** `/api/v1/auth/2fa`

جميع نقاط النهاية تتطلب المصادقة.

#### POST `/api/v1/auth/2fa/setup`

توليد سر TOTP ورابط رمز QR لإعداد المصادقة الثنائية.

**المصادقة:** مصادق عليه

**الاستجابة (200):**

```json
{
  "secret": "JBSWY3DPEHPK3PXP",
  "qrCodeUri": "otpauth://totp/AuthSystem:user@example.com?secret=JBSWY3DPEHPK3PXP&issuer=AuthSystem&digits=6&period=30"
}
```

> يقوم المستخدم بمسح رمز QR بتطبيق المصادقة (Google Authenticator، Authy، إلخ.) ثم يستدعي نقطة نهاية التفعيل مع رمز صالح.

#### POST `/api/v1/auth/2fa/enable`

تفعيل المصادقة الثنائية بعد التحقق من رمز TOTP.

**المصادقة:** مصادق عليه

**الطلب:**

```json
{
  "code": "123456"
}
```

**الاستجابة (200):**

```json
{
  "recoveryCodes": [
    "ABCD-1234-EFGH",
    "IJKL-5678-MNOP",
    "QRST-9012-UVWX"
  ]
}
```

> **هام:** أكواد الاسترداد تُعرض مرة واحدة فقط. يجب على المستخدم حفظها بأمان.

#### POST `/api/v1/auth/2fa/disable`

تعطيل المصادقة الثنائية (يتطلب رمز TOTP صالح للتأكيد).

**المصادقة:** مصادق عليه

**الطلب:**

```json
{
  "code": "123456"
}
```

**الاستجابة:** 204 بلا محتوى

---

### 5.4 المستخدمون

**المسار الأساسي:** `/api/v1/users`

جميع نقاط النهاية تتطلب المصادقة وصلاحيات محددة.

#### GET `/api/v1/users`

عرض المستخدمين مع ترقيم الصفحات والبحث.

**الصلاحية:** `users:read`

**معاملات الاستعلام:**

| المعامل | النوع | الافتراضي | الوصف |
|---|---|---|---|
| `pageNumber` | int | 1 | رقم الصفحة |
| `pageSize` | int | 10 | العناصر لكل صفحة |
| `searchTerm` | string | null | البحث بالاسم أو البريد الإلكتروني |

**الاستجابة (200):**

```json
{
  "items": [
    {
      "id": "3fa85f64-...",
      "email": "user@example.com",
      "firstName": "John",
      "lastName": "Doe",
      "displayName": "John Doe",
      "status": "Active",
      "emailConfirmed": true,
      "twoFactorEnabled": false,
      "createdAt": "2026-01-01T00:00:00Z"
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 42,
  "totalPages": 5
}
```

#### GET `/api/v1/users/{id}`

الحصول على مستخدم بالمعرف.

**الصلاحية:** `users:read`

**الاستجابة (200):** `UserDto`

#### POST `/api/v1/users`

إنشاء مستخدم جديد (بواسطة المسؤول).

**الصلاحية:** `users:create`

**الطلب:**

```json
{
  "email": "newuser@example.com",
  "password": "SecureP@ssw0rd!",
  "firstName": "Jane",
  "lastName": "Doe",
  "displayName": "Jane Doe",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "UTC",
  "roleIds": ["role-guid-1", "role-guid-2"]
}
```

**الاستجابة (201):** `UserDto`

#### PUT `/api/v1/users/{id}`

تحديث معلومات ملف المستخدم.

**الصلاحية:** `users:update`

**الطلب:**

```json
{
  "firstName": "Jane",
  "lastName": "Smith",
  "displayName": "Jane Smith",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "ar",
  "timeZone": "Asia/Riyadh"
}
```

**الاستجابة (200):** `UserDto`

#### DELETE `/api/v1/users/{id}`

حذف مستخدم (حذف ناعم).

**الصلاحية:** `users:delete`

**الاستجابة:** 204 بلا محتوى

> لا يمكن حذف مستخدمي النظام.

#### POST `/api/v1/users/{id}/roles`

تعيين دور لمستخدم.

**الصلاحية:** `users:manage-roles`

**الطلب:**

```json
{
  "roleId": "role-guid",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

| الحقل | الوصف |
|---|---|
| `expiresAt` | اختياري؛ إذا حُدد، تنتهي صلاحية تعيين الدور في هذا الوقت |

**الاستجابة:** 204 بلا محتوى

#### GET `/api/v1/users/{id}/roles`

الحصول على جميع الأدوار المعينة لمستخدم.

**الصلاحية:** `users:read`

**الاستجابة (200):**

```json
[
  {
    "roleId": "role-guid",
    "roleName": "Admin",
    "roleCode": "admin",
    "applicationId": null,
    "assignedAt": "2026-01-01T00:00:00Z",
    "expiresAt": null
  }
]
```

#### DELETE `/api/v1/users/{id}/roles/{roleId}`

إزالة دور من مستخدم.

**الصلاحية:** `users:manage-roles`

**الاستجابة:** 204 بلا محتوى

#### GET `/api/v1/users/{id}/permissions`

الحصول على جميع صلاحيات المستخدم (مباشرة + موروثة من الأدوار).

**الصلاحية:** `users:read`

**الاستجابة (200):**

```json
[
  {
    "permissionId": "perm-guid",
    "permissionCode": "users:read",
    "permissionName": "قراءة المستخدمين",
    "source": "direct",
    "applicationId": null,
    "expiresAt": null
  }
]
```

#### POST `/api/v1/users/{id}/permissions`

منح صلاحية مباشرة لمستخدم.

**الصلاحية:** `users:manage-permissions`

**الطلب:**

```json
{
  "permissionId": "perm-guid",
  "applicationId": "app-guid",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

**الاستجابة:** 204 بلا محتوى

#### DELETE `/api/v1/users/{id}/permissions/{permissionId}`

سحب صلاحية ممنوحة مباشرة من مستخدم.

**الصلاحية:** `users:manage-permissions`

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/users/{id}/lock`

قفل حساب مستخدم.

**الصلاحية:** `users:manage`

**الطلب:**

```json
{
  "reason": "تم اكتشاف نشاط مشبوه",
  "lockDurationMinutes": 60
}
```

| الحقل | الوصف |
|---|---|
| `lockDurationMinutes` | اختياري؛ إذا حُذف، يُقفل الحساب لأجل غير مسمى حتى الفتح اليدوي |

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/users/{id}/unlock`

فتح حساب مستخدم مقفل.

**الصلاحية:** `users:manage`

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/users/{id}/activate`

تفعيل حساب مستخدم معطل.

**الصلاحية:** `users:manage`

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/users/{id}/deactivate`

تعطيل حساب مستخدم.

**الصلاحية:** `users:manage`

**الاستجابة:** 204 بلا محتوى

#### GET `/api/v1/users/me`

الحصول على الملف الشخصي للمستخدم المصادق عليه.

**الصلاحية:** لا شيء (المصادقة فقط)

**الاستجابة (200):** `UserDto`

#### PUT `/api/v1/users/me`

تحديث الملف الشخصي للمستخدم المصادق عليه.

**الصلاحية:** لا شيء (المصادقة فقط)

**الطلب:**

```json
{
  "firstName": "John",
  "lastName": "Doe",
  "displayName": "Johnny",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "America/New_York"
}
```

جميع الحقول اختيارية.

**الاستجابة (200):** `UserDto`

---

### 5.5 الأدوار

**المسار الأساسي:** `/api/v1/roles`

#### GET `/api/v1/roles`

عرض جميع الأدوار، مع إمكانية التصفية حسب التطبيق.

**الصلاحية:** `roles:read`

**معاملات الاستعلام:**

| المعامل | النوع | الوصف |
|---|---|---|
| `applicationId` | Guid? | تصفية الأدوار حسب التطبيق (null = أدوار عامة) |

**الاستجابة (200):**

```json
[
  {
    "id": "role-guid",
    "code": "admin",
    "name": "مسؤول",
    "description": "وصول كامل للنظام",
    "applicationId": null,
    "isSystem": true,
    "isActive": true,
    "permissionCount": 15
  }
]
```

#### GET `/api/v1/roles/{id}`

الحصول على دور بالمعرف.

**الصلاحية:** `roles:read`

**الاستجابة (200):** `RoleDto` (يتضمن قائمة الصلاحيات)

#### POST `/api/v1/roles`

إنشاء دور جديد.

**الصلاحية:** `roles:create`

**الطلب:**

```json
{
  "applicationId": "app-guid-or-null",
  "code": "editor",
  "name": "محرر المحتوى",
  "description": "يمكنه تحرير ونشر المحتوى",
  "permissionIds": ["perm-guid-1", "perm-guid-2"]
}
```

| الحقل | الوصف |
|---|---|
| `applicationId` | Null للأدوار العامة؛ اضبطه لتحديد نطاق الدور لتطبيق معين |
| `code` | كود فريد ضمن نطاق التطبيق |
| `permissionIds` | اختياري؛ الصلاحيات لتعيينها للدور |

**الاستجابة (201):** `RoleDto`

#### PUT `/api/v1/roles/{id}`

تحديث دور.

**الصلاحية:** `roles:update`

**الطلب:**

```json
{
  "name": "محرر أول",
  "description": "يمكنه تحرير ونشر واعتماد المحتوى"
}
```

**الاستجابة (200):** `RoleDto`

#### DELETE `/api/v1/roles/{id}`

حذف دور.

**الصلاحية:** `roles:delete`

**الاستجابة:** 204 بلا محتوى

> لا يمكن حذف أدوار النظام.

---

### 5.6 الصلاحيات

**المسار الأساسي:** `/api/v1/permissions`

#### GET `/api/v1/permissions`

عرض جميع الصلاحيات، مع إمكانية التصفية حسب التطبيق.

**الصلاحية:** `permissions:read`

**معاملات الاستعلام:**

| المعامل | النوع | الوصف |
|---|---|---|
| `applicationId` | Guid? | التصفية حسب التطبيق |

**الاستجابة (200):**

```json
[
  {
    "id": "perm-guid",
    "code": "users:read",
    "name": "قراءة المستخدمين",
    "description": "عرض ملفات المستخدمين",
    "applicationId": null,
    "parentId": null,
    "level": 3,
    "isWildcard": false,
    "isActive": true
  }
]
```

#### GET `/api/v1/permissions/{id}`

الحصول على صلاحية بالمعرف.

**الصلاحية:** `permissions:read`

**الاستجابة (200):** `PermissionDto`

#### POST `/api/v1/permissions`

إنشاء صلاحية جديدة.

**الصلاحية:** `permissions:create`

**الطلب:**

```json
{
  "applicationId": "app-guid",
  "code": "crm:leads:read",
  "name": "قراءة العملاء المحتملين",
  "description": "عرض عملاء CRM المحتملين",
  "parentId": "parent-perm-guid"
}
```

**تسلسل كود الصلاحيات:**
- المستوى 0: `*` (بدل شامل)
- المستوى 1: `crm:*` (بدل التطبيق)
- المستوى 2: `crm:leads:*` (بدل المورد)
- المستوى 3: `crm:leads:read` (إجراء محدد)

**الاستجابة (201):** `PermissionDto`

#### PUT `/api/v1/permissions/{id}`

تحديث صلاحية.

**الصلاحية:** `permissions:update`

**الطلب:**

```json
{
  "name": "عرض العملاء المحتملين",
  "description": "عرض عملاء CRM المحتملين وتفاصيلهم"
}
```

**الاستجابة (200):** `PermissionDto`

#### DELETE `/api/v1/permissions/{id}`

حذف صلاحية.

**الصلاحية:** `permissions:delete`

**الاستجابة:** 204 بلا محتوى

#### GET `/api/v1/permissions/{id}/implications`

الحصول على الصلاحيات الضمنية لصلاحية معينة.

**الصلاحية:** `permissions:read`

**الاستجابة (200):** `PermissionDto[]`

> مثال: `users:manage` قد تتضمن ضمنياً `users:read`، مما يعني أن أي شخص لديه `users:manage` يحصل تلقائياً على `users:read`.

#### POST `/api/v1/permissions/{id}/implications`

إضافة تضمين صلاحية.

**الصلاحية:** `permissions:manage`

**الطلب:**

```json
{
  "impliedPermissionId": "implied-perm-guid"
}
```

**الاستجابة:** 201 تم الإنشاء

#### DELETE `/api/v1/permissions/{id}/implications/{impliedId}`

إزالة تضمين صلاحية.

**الصلاحية:** `permissions:manage`

**الاستجابة:** 204 بلا محتوى

---

### 5.7 التطبيقات

**المسار الأساسي:** `/api/v1/applications`

التطبيقات تمثل الأنظمة/الخدمات المختلفة التي تستخدم AuthSystem للهوية.

#### GET `/api/v1/applications`

عرض التطبيقات مع ترقيم الصفحات.

**الصلاحية:** `applications:read`

**معاملات الاستعلام:**

| المعامل | النوع | الافتراضي | الوصف |
|---|---|---|---|
| `pageNumber` | int | 1 | رقم الصفحة |
| `pageSize` | int | 10 | العناصر لكل صفحة |
| `search` | string | null | البحث بالاسم أو الكود |
| `isActive` | bool? | null | التصفية حسب حالة النشاط |

**الاستجابة (200):**

```json
{
  "items": [
    {
      "id": "app-guid",
      "code": "CRM",
      "name": "إدارة علاقات العملاء",
      "description": "تطبيق CRM",
      "baseUrl": "https://crm.yourdomain.com",
      "logoUrl": "https://...",
      "contactEmail": "crm@yourdomain.com",
      "isActive": true,
      "allowSelfRegistration": false,
      "requireTwoFactor": false,
      "requireEmailVerification": true,
      "sessionTimeoutMinutes": 60,
      "maxConcurrentSessions": 5
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 3,
  "totalPages": 1
}
```

#### GET `/api/v1/applications/{id}`

الحصول على تطبيق بالمعرف.

**الصلاحية:** `applications:read`

**الاستجابة (200):** `ApplicationDto`

#### GET `/api/v1/applications/{id}/roles`

الحصول على جميع الأدوار المحددة النطاق لتطبيق.

**الصلاحية:** `applications:read`

**الاستجابة (200):** `RoleDto[]`

#### GET `/api/v1/applications/{id}/permissions`

الحصول على جميع الصلاحيات المحددة النطاق لتطبيق.

**الصلاحية:** `applications:read`

**الاستجابة (200):** `PermissionDto[]`

#### POST `/api/v1/applications`

إنشاء تطبيق جديد.

**الصلاحية:** `applications:create`

**الطلب:**

```json
{
  "code": "CRM",
  "name": "إدارة علاقات العملاء",
  "description": "تطبيق CRM لإدارة العملاء المحتملين وجهات الاتصال",
  "baseUrl": "https://crm.yourdomain.com",
  "logoUrl": "https://...",
  "contactEmail": "crm@yourdomain.com",
  "allowSelfRegistration": false,
  "requireTwoFactor": false,
  "requireEmailVerification": true,
  "sessionTimeoutMinutes": 60,
  "maxConcurrentSessions": 5
}
```

**الاستجابة (201):** `ApplicationDto`

#### PUT `/api/v1/applications/{id}`

تحديث تطبيق.

**الصلاحية:** `applications:update`

**الطلب:** نفس حقول الإنشاء (باستثناء `code` الذي لا يمكن تغييره).

**الاستجابة (200):** `ApplicationDto`

#### DELETE `/api/v1/applications/{id}`

حذف تطبيق.

**الصلاحية:** `applications:delete`

**الاستجابة:** 204 بلا محتوى

---

### 5.8 المؤسسات

**المسار الأساسي:** `/api/v1/organizations`

المؤسسات توفر تعدد المستأجرين — المستخدمون ينتمون لمؤسسات، والمؤسسات تشترك في التطبيقات.

#### GET `/api/v1/organizations`

عرض المؤسسات التي ينتمي إليها المستخدم المصادق عليه.

**الصلاحية:** لا شيء (المصادقة فقط)

**الاستجابة (200):**

```json
[
  {
    "id": "org-guid",
    "code": "acme-corp",
    "name": "شركة أكمي",
    "contactEmail": "admin@acme.com",
    "isActive": true,
    "memberCount": 25,
    "role": "org-owner"
  }
]
```

#### GET `/api/v1/organizations/{id}`

الحصول على تفاصيل المؤسسة.

**الصلاحية:** لا شيء (يجب أن يكون عضواً)

**الاستجابة (200):**

```json
{
  "id": "org-guid",
  "code": "acme-corp",
  "name": "شركة أكمي",
  "description": "شركة تقنية رائدة",
  "logoUrl": "https://...",
  "website": "https://acme.com",
  "contactEmail": "admin@acme.com",
  "ownerId": "user-guid",
  "isActive": true,
  "isAutoCreated": false,
  "createdAt": "2026-01-01T00:00:00Z",
  "memberCount": 25,
  "applicationCount": 3
}
```

#### POST `/api/v1/organizations`

إنشاء مؤسسة جديدة.

**الصلاحية:** لا شيء (المصادقة فقط)

**الطلب:**

```json
{
  "code": "acme-corp",
  "name": "شركة أكمي",
  "contactEmail": "admin@acme.com",
  "description": "شركة تقنية رائدة",
  "logoUrl": "https://...",
  "website": "https://acme.com"
}
```

**الاستجابة (201):** `OrganizationDto`

> المستخدم المنشئ يصبح مالك المؤسسة تلقائياً.

#### PUT `/api/v1/organizations/{id}`

تحديث تفاصيل المؤسسة.

**الصلاحية:** `org:update`

**الطلب:**

```json
{
  "name": "شركة أكمي الدولية",
  "contactEmail": "global@acme.com",
  "description": "وصف محدث",
  "logoUrl": "https://...",
  "website": "https://acme.global",
  "isActive": true
}
```

**الاستجابة (200):** `OrganizationDto`

#### DELETE `/api/v1/organizations/{id}`

حذف مؤسسة.

**الصلاحية:** لا شيء (يجب أن يكون المالك)

**الاستجابة:** 204 بلا محتوى

#### GET `/api/v1/organizations/{id}/members`

عرض أعضاء المؤسسة مع ترقيم الصفحات.

**الصلاحية:** `org:members:read`

**معاملات الاستعلام:**

| المعامل | النوع | الافتراضي | الوصف |
|---|---|---|---|
| `pageNumber` | int | 1 | رقم الصفحة |
| `pageSize` | int | 10 | العناصر لكل صفحة |
| `search` | string | null | البحث بالاسم أو البريد الإلكتروني |

**الاستجابة (200):**

```json
{
  "items": [
    {
      "userId": "user-guid",
      "email": "member@acme.com",
      "displayName": "جون دو",
      "roleId": "role-guid",
      "roleName": "org-admin",
      "joinedAt": "2026-01-15T00:00:00Z",
      "isActive": true
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 25,
  "totalPages": 3
}
```

#### PUT `/api/v1/organizations/{orgId}/members/{userId}/role`

تغيير دور عضو في المؤسسة.

**الصلاحية:** `org:members:manage`

**الطلب:**

```json
{
  "roleId": "new-role-guid"
}
```

**الاستجابة (200):** `OrganizationMemberDto`

#### DELETE `/api/v1/organizations/{orgId}/members/{userId}`

إزالة عضو من المؤسسة.

**الصلاحية:** `org:members:manage`

**الاستجابة:** 204 بلا محتوى

#### GET `/api/v1/organizations/{id}/invitations`

عرض الدعوات المعلقة لمؤسسة.

**الصلاحية:** `org:members:read`

**الاستجابة (200):**

```json
[
  {
    "id": "invitation-guid",
    "email": "invitee@example.com",
    "roleId": "role-guid",
    "roleName": "org-member",
    "status": "Pending",
    "invitedBy": "user-guid",
    "invitedAt": "2026-03-10T00:00:00Z",
    "expiresAt": "2026-03-17T00:00:00Z"
  }
]
```

#### POST `/api/v1/organizations/{id}/invitations`

دعوة مستخدم للانضمام إلى المؤسسة.

**الصلاحية:** `org:members:invite`

**الطلب:**

```json
{
  "email": "invitee@example.com",
  "roleId": "role-guid"
}
```

**الاستجابة (201):** `OrganizationInvitationDto`

#### GET `/api/v1/organizations/{id}/applications`

عرض التطبيقات المفعلة للمؤسسة.

**الصلاحية:** `org:apps:read`

**الاستجابة (200):**

```json
[
  {
    "applicationId": "app-guid",
    "applicationName": "CRM",
    "subscriptionTier": "Enterprise",
    "isActive": true,
    "enabledAt": "2026-01-01T00:00:00Z",
    "expiresAt": "2027-01-01T00:00:00Z"
  }
]
```

#### POST `/api/v1/organizations/{id}/applications`

تفعيل تطبيق للمؤسسة.

**الصلاحية:** `org:apps:manage`

**الطلب:**

```json
{
  "applicationId": "app-guid",
  "subscriptionTier": "Enterprise",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**الاستجابة (201):** `OrganizationApplicationDto`

#### PUT `/api/v1/organizations/{id}/applications/{applicationId}`

تحديث اشتراك تطبيق للمؤسسة.

**الصلاحية:** `org:apps:manage`

**الطلب:**

```json
{
  "subscriptionTier": "Premium",
  "expiresAt": "2027-06-01T00:00:00Z",
  "isActive": true
}
```

**الاستجابة (200):** `OrganizationApplicationDto`

#### DELETE `/api/v1/organizations/{id}/applications/{applicationId}`

تعطيل تطبيق للمؤسسة.

**الصلاحية:** `org:apps:manage`

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/organizations/{orgId}/members/{userId}/roles`

تعيين دور خاص بتطبيق لعضو ضمن سياق المؤسسة.

**الصلاحية:** `org:permissions:manage`

**الطلب:**

```json
{
  "applicationId": "app-guid",
  "roleId": "role-guid",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**الاستجابة (201):** `OrganizationMemberAppRoleDto`

#### POST `/api/v1/organizations/{orgId}/members/{userId}/permissions`

منح صلاحية لعضو ضمن سياق المؤسسة.

**الصلاحية:** `org:permissions:manage`

**الطلب:**

```json
{
  "applicationId": "app-guid",
  "permissionId": "perm-guid",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**الاستجابة (201):** `OrganizationMemberPermissionDto`

---

### 5.9 الدعوات

**المسار الأساسي:** `/api/v1/invitations`

#### POST `/api/v1/invitations/{token}/accept`

قبول دعوة مؤسسة باستخدام رمز الدعوة.

**المصادقة:** مصادق عليه

**الاستجابة (200):**

```json
{
  "organizationId": "org-guid",
  "organizationName": "شركة أكمي",
  "role": "org-member"
}
```

---

### 5.10 مفاتيح API

**المسار الأساسي:** `/api/v1/apikeys`

مفاتيح API توفر وصولاً برمجياً للتطبيقات والخدمات.

#### GET `/api/v1/apikeys`

عرض مفاتيح API لتطبيق.

**الصلاحية:** `apikeys:read`

**معاملات الاستعلام:**

| المعامل | النوع | الوصف |
|---|---|---|
| `applicationId` | Guid | التصفية حسب التطبيق (مطلوب) |

**الاستجابة (200):**

```json
[
  {
    "id": "key-guid",
    "applicationId": "app-guid",
    "name": "مفتاح API للإنتاج",
    "description": "المفتاح الرئيسي للإنتاج",
    "keyPrefix": "ak_prod_",
    "environment": "production",
    "rateLimitPerMinute": 100,
    "rateLimitPerDay": 10000,
    "createdAt": "2026-01-01T00:00:00Z",
    "expiresAt": "2027-01-01T00:00:00Z",
    "lastUsedAt": "2026-03-12T14:30:00Z",
    "isRevoked": false
  }
]
```

#### POST `/api/v1/apikeys`

إنشاء مفتاح API جديد.

**الصلاحية:** `apikeys:create`

**الطلب:**

```json
{
  "applicationId": "app-guid",
  "name": "مفتاح API للإنتاج",
  "description": "المفتاح الرئيسي للإنتاج لتكامل CRM",
  "environment": "production",
  "rateLimitPerMinute": 100,
  "rateLimitPerDay": 10000,
  "expiresAt": "2027-01-01T00:00:00Z",
  "permissionIds": ["perm-guid-1", "perm-guid-2"]
}
```

| قيم `environment` | الوصف |
|---|---|
| `production` | بيئة الإنتاج |
| `staging` | بيئة الاختبار |
| `development` | بيئة التطوير |

**الاستجابة (201):**

```json
{
  "id": "key-guid",
  "apiKey": "ak_prod_AbCdEfGhIjKlMnOpQrStUvWxYz...",
  "message": "احفظ مفتاح API هذا بأمان. لن يُعرض مرة أخرى."
}
```

> **هام:** مفتاح API بالنص العادي يُعاد مرة واحدة فقط عند الإنشاء. يُخزن كتجزئة Argon2id في قاعدة البيانات.

#### POST `/api/v1/apikeys/{id}/revoke`

إبطال مفتاح API.

**الصلاحية:** `apikeys:revoke`

**الطلب:**

```json
{
  "reason": "تم اكتشاف مفتاح مخترق"
}
```

**الاستجابة:** 204 بلا محتوى

#### POST `/api/v1/apikeys/{id}/rotate`

تدوير مفتاح API (إنشاء جديد، جدولة القديم للإبطال).

**الصلاحية:** `apikeys:rotate`

**الطلب:**

```json
{
  "gracePeriodMinutes": 60
}
```

| الحقل | الوصف |
|---|---|
| `gracePeriodMinutes` | الوقت قبل إبطال المفتاح القديم تلقائياً (افتراضي: 60) |

**الاستجابة (200):**

```json
{
  "newApiKey": "ak_prod_NewKeyValue...",
  "oldKeyExpiresAt": "2026-03-12T15:30:00Z",
  "message": "تم توليد مفتاح جديد. المفتاح القديم سيُبطل بعد فترة السماح."
}
```

---

### 5.11 سجلات التدقيق

**المسار الأساسي:** `/api/v1/audit-logs`

سجل تدقيق شامل لجميع عمليات النظام.

#### GET `/api/v1/audit-logs`

استعلام سجلات التدقيق مع فلاتر.

**الصلاحية:** `auditlogs:read`

**معاملات الاستعلام:**

| المعامل | النوع | الوصف |
|---|---|---|
| `pageNumber` | int | رقم الصفحة (افتراضي: 1) |
| `pageSize` | int | العناصر لكل صفحة (افتراضي: 10) |
| `userId` | Guid? | التصفية حسب المستخدم |
| `applicationId` | Guid? | التصفية حسب التطبيق |
| `actionType` | string? | التصفية حسب نوع الإجراء (مثل "Authentication"، "UserManagement") |
| `action` | string? | التصفية حسب إجراء محدد (مثل "user.login"، "password.changed") |
| `fromDate` | DateTime? | فلتر تاريخ البداية |
| `toDate` | DateTime? | فلتر تاريخ النهاية |
| `isSuccess` | bool? | التصفية حسب النجاح/الفشل |

**الاستجابة (200):**

```json
{
  "items": [
    {
      "id": "log-guid",
      "userId": "user-guid",
      "applicationId": null,
      "action": "user.login",
      "actionType": "Authentication",
      "entityType": "User",
      "entityId": "user-guid",
      "ipAddress": "192.168.1.1",
      "userAgent": "Mozilla/5.0...",
      "isSuccess": true,
      "timestamp": "2026-03-12T14:30:00Z"
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 1500,
  "totalPages": 150
}
```

#### GET `/api/v1/audit-logs/{id}`

الحصول على سجل تدقيق محدد مع التفاصيل الكاملة.

**الصلاحية:** `auditlogs:read`

**الاستجابة (200):**

```json
{
  "id": "log-guid",
  "userId": "user-guid",
  "action": "user.updated",
  "entityType": "User",
  "entityId": "target-user-guid",
  "oldValues": "{\"firstName\": \"John\"}",
  "newValues": "{\"firstName\": \"Jonathan\"}",
  "ipAddress": "192.168.1.1",
  "userAgent": "Mozilla/5.0...",
  "isSuccess": true,
  "timestamp": "2026-03-12T14:30:00Z",
  "correlationId": "abc-123"
}
```

#### GET `/api/v1/audit-logs/users/{userId}`

الحصول على سجلات التدقيق لمستخدم محدد.

**الصلاحية:** `auditlogs:read`

**معاملات الاستعلام:** `pageNumber`، `pageSize`، `fromDate`، `toDate`

**الاستجابة (200):** `PagedAuditLogsDto`

#### GET `/api/v1/audit-logs/entities/{entityType}/{entityId}`

الحصول على سجلات التدقيق لكيان محدد (مثل جميع التغييرات على دور معين).

**الصلاحية:** `auditlogs:read`

**الاستجابة (200):** `AuditLogDto[]`

#### POST `/api/v1/audit-logs/export`

تصدير سجلات التدقيق إلى ملف.

**الصلاحية:** `auditlogs:export`

**الطلب:**

```json
{
  "format": "csv",
  "userId": null,
  "applicationId": null,
  "actionType": "Authentication",
  "action": null,
  "fromDate": "2026-01-01T00:00:00Z",
  "toDate": "2026-03-12T23:59:59Z",
  "isSuccess": null,
  "maxRecords": 10000
}
```

| قيم `format` | الوصف |
|---|---|
| `csv` | ملف قيم مفصولة بفواصل |
| `json` | ملف مصفوفة JSON |

**الاستجابة:** تحميل ملف (Content-Type: `text/csv` أو `application/json`)

---

### 5.12 الأسرار (المسؤول)

**المسار الأساسي:** `/api/v1/admin/secrets`

**المتطلبات:** `SecretManagement:EnableAdminApi` يجب أن يكون `true` في الإعدادات (أبقِه معطلاً إلا أثناء التزويد)، ويجب أن تُرسَل الطلبات عبر HTTPS — لأن `generate/*` و`import/*` تحمل مادة مفاتيح خاصة.

جميع نقاط النهاية تتطلب صلاحية `secrets.manage`. تجعل `generate/*` **النظام** يولّد مفتاحاً جديداً؛ بينما `import/*` تخزّن قيمة **تورّدها أنت** (BYOK) وتعمل فقط في وضعَي Certificate/Dpapi.

#### GET `/api/v1/admin/secrets/status`

الحصول على حالة جميع أسرار النظام (لا تُكشف القيم).

**الصلاحية:** `secrets.manage`

**الاستجابة (200):**

```json
{
  "rsaKeyConfigured": true,
  "hmacKeyConfigured": true,
  "gatewayTokenConfigured": true,
  "smtpPasswordConfigured": false,
  "customSecrets": ["Custom:ApiIntegrationKey"],
  "secretsFilePath": "C:\\Users\\...\\secrets.dpapi",
  "lastModified": "2026-03-01T10:00:00Z"
}
```

#### POST `/api/v1/admin/secrets/generate/rsa`

توليد زوج مفاتيح RSA جديد (يستبدل الموجود).

**الصلاحية:** `secrets.manage`

**الاستجابة (200):**

```json
{
  "publicKey": "-----BEGIN PUBLIC KEY-----\nMIIBIjAN...",
  "message": "تم توليد زوج مفاتيح RSA. تحذير: جميع رموز الوصول الحالية أصبحت غير صالحة."
}
```

> **تحذير:** إعادة توليد مفاتيح RSA تُبطل جميع رموز الوصول النشطة. سيحتاج جميع المستخدمين لتحديث رموزهم.

#### POST `/api/v1/admin/secrets/generate/hmac`

توليد مفتاح HMAC جديد (يستبدل الموجود).

**الصلاحية:** `secrets.manage`

**الاستجابة (200):**

```json
{
  "message": "تم توليد مفتاح HMAC. تحذير: جميع رموز التحديث الحالية أصبحت غير صالحة."
}
```

> **تحذير:** إعادة توليد مفتاح HMAC تُبطل جميع رموز التحديث. سيحتاج جميع المستخدمين لإعادة المصادقة.

#### POST `/api/v1/admin/secrets/generate/gateway-token`

توليد رمز بوابة جديد.

**الصلاحية:** `secrets.manage`

**الاستجابة (200):**

```json
{
  "message": "تم توليد رمز البوابة. حدّث إعدادات بوابة API لاستخدام الرمز الجديد."
}
```

> **هام:** بعد إعادة التوليد، يجب إعادة تشغيل بوابة API لالتقاط الرمز الجديد.

#### POST `/api/v1/admin/secrets/import/rsa`

استيراد مفتاح RSA **خاص** تورّده أنت لتوقيع JWT (مفاتيحك الخاصة — BYOK). يُشتق المفتاح العام المطابق ويُخزَّن تلقائياً.

**الصلاحية:** `secrets.manage`

**الطلب:**

```json
{
  "value": "-----BEGIN PRIVATE KEY-----\nMIIEvg...\n-----END PRIVATE KEY-----"
}
```

> ورّد المفتاح بصيغة PEM (PKCS#8 أو PKCS#1، ≥ 2048 بت) مع تهريب أسطر الجديدة كـ `\n` في JSON.

**الاستجابة (200):**

```json
{
  "success": true,
  "message": "RSA signing key imported successfully. All existing access tokens are now invalid. Users must re-authenticate.",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n..."
}
```

> **متطلب وضع التخزين:** `import/*` يعمل فقط في وضع **Certificate** أو **Dpapi**. في وضع **PlainText** يعيد `409 Secret.ImportNotSupportedInPlainText` — حرّر المفاتيح مباشرة في `appsettings.Production.json` بدلاً من ذلك. الاستيراد **يستبدل** المفتاح الحالي؛ وإعادة استيراد **نفس** القيمة عملية آمنة لا تؤثر على الرموز الحية.

#### POST `/api/v1/admin/secrets/import/hmac`

استيراد مفتاح HMAC تورّده أنت لتجزئة رموز التحديث (BYOK).

**الصلاحية:** `secrets.manage`

**الطلب:**

```json
{
  "value": "<مفتاح بترميز base64، >= 32 بايت>"
}
```

**الاستجابة (200):**

```json
{
  "success": true,
  "message": "HMAC key imported successfully. All existing refresh tokens are now invalid. Users must re-authenticate."
}
```

> وضعا Certificate/Dpapi فقط (`409` في PlainText). يستبدل المفتاح الحالي.

#### POST `/api/v1/admin/secrets/import/gateway-token`

استيراد رمز بوابة تورّده أنت للمصادقة بين الخدمات (BYOK).

**الصلاحية:** `secrets.manage`

**الطلب:**

```json
{
  "value": "<رمز البوابة، >= 16 حرفاً>"
}
```

**الاستجابة (200):**

```json
{
  "success": true,
  "message": "Gateway token imported successfully. Update the API Gateway configuration with the same token."
}
```

> وضعا Certificate/Dpapi فقط (`409` في PlainText). يجب تكوين بوابة API بنفس الرمز.

#### PUT `/api/v1/admin/secrets/custom/{key}`

تعيين قيمة سر مخصص.

**الصلاحية:** `secrets.manage`

**معامل المسار:** `key` — أحرف أبجدية رقمية وشرطات سفلية ونقاط فقط (بحد أقصى 100 حرف)

**الطلب:**

```json
{
  "value": "my-secret-value"
}
```

**الاستجابة:** 204 بلا محتوى

> الأسرار المخصصة تُخزن تحت مساحة الاسم `Custom:` (مثل `Custom:my.api.key`).

#### DELETE `/api/v1/admin/secrets/custom/{key}`

حذف سر مخصص.

**الصلاحية:** `secrets.manage`

**الاستجابة:** 204 بلا محتوى

---

## 6. سيناريوهات العمل الشائعة

### 6.1 التسجيل → التحقق من البريد → تسجيل الدخول

```
الخطوة 1: التسجيل
POST /api/v1/auth/register
Body: { email, password, firstName, lastName }
→ 201: { userId, maskedEmail }

الخطوة 2: إرسال بريد التحقق (إذا كانت خدمة البريد مفعلة)
POST /api/v1/auth/send-verification-email
Header: Authorization: Bearer <access_token>
→ 200: { expiresAt, maskedEmail }

الخطوة 3: التحقق من البريد بـ OTP
POST /api/v1/auth/verify-email
Body: { userId, otp: "123456" }
→ 204

الخطوة 4: تسجيل الدخول
POST /api/v1/auth/login
Body: { email, password }
→ 200: { token: { accessToken, refreshToken }, user: {...} }
```

### 6.2 تفعيل المصادقة الثنائية

```
الخطوة 1: الإعداد (الحصول على سر TOTP)
POST /api/v1/auth/2fa/setup
Header: Authorization: Bearer <access_token>
→ 200: { secret, qrCodeUri }

الخطوة 2: مسح رمز QR بتطبيق المصادقة

الخطوة 3: التفعيل برمز التحقق
POST /api/v1/auth/2fa/enable
Header: Authorization: Bearer <access_token>
Body: { code: "123456" }
→ 200: { recoveryCodes: [...] }

الخطوة 4: حفظ أكواد الاسترداد بأمان
```

### 6.3 نسيان كلمة المرور → إعادة التعيين

```
الخطوة 1: طلب إعادة تعيين كلمة المرور
POST /api/v1/auth/forgot-password
Body: { email: "user@example.com" }
→ 200: { message, maskedEmail }

الخطوة 2: المستخدم يستلم بريداً إلكترونياً برمز إعادة التعيين

الخطوة 3: إعادة تعيين كلمة المرور
POST /api/v1/auth/reset-password
Body: { email, token, newPassword, confirmNewPassword, terminateSessions: true }
→ 204
```

### 6.4 دعوة مستخدم إلى مؤسسة

```
الخطوة 1: إرسال الدعوة
POST /api/v1/organizations/{orgId}/invitations
Header: Authorization: Bearer <admin_token>
Body: { email: "invitee@example.com", roleId: "member-role-guid" }
→ 201: { invitationId, token, expiresAt }

الخطوة 2: المدعو يستلم بريداً إلكترونياً برابط/رمز الدعوة

الخطوة 3: المدعو يقبل الدعوة
POST /api/v1/invitations/{token}/accept
Header: Authorization: Bearer <invitee_token>
→ 200: { organizationId, organizationName, role }
```

### 6.5 إعداد تطبيق مع أدوار وصلاحيات

```
الخطوة 1: إنشاء التطبيق
POST /api/v1/applications
Body: { code: "CRM", name: "نظام CRM", ... }
→ 201: { id: "app-guid", ... }

الخطوة 2: إنشاء صلاحيات للتطبيق
POST /api/v1/permissions
Body: { applicationId: "app-guid", code: "crm:leads:read", name: "قراءة العملاء المحتملين" }
POST /api/v1/permissions
Body: { applicationId: "app-guid", code: "crm:leads:create", name: "إنشاء عملاء محتملين" }
POST /api/v1/permissions
Body: { applicationId: "app-guid", code: "crm:leads:*", name: "جميع عمليات العملاء المحتملين" }

الخطوة 3: إنشاء أدوار مع صلاحيات
POST /api/v1/roles
Body: { applicationId: "app-guid", code: "crm-viewer", name: "عارض CRM", permissionIds: ["read-perm-guid"] }
POST /api/v1/roles
Body: { applicationId: "app-guid", code: "crm-editor", name: "محرر CRM", permissionIds: ["read-guid", "create-guid"] }

الخطوة 4: تعيين دور لمستخدم
POST /api/v1/users/{userId}/roles
Body: { roleId: "crm-editor-guid" }
→ 204
```

### 6.6 تدوير مفتاح API

```
الخطوة 1: تدوير المفتاح (كلا القديم والجديد صالحان خلال فترة السماح)
POST /api/v1/apikeys/{keyId}/rotate
Body: { gracePeriodMinutes: 120 }
→ 200: { newApiKey: "ak_prod_...", oldKeyExpiresAt: "..." }

الخطوة 2: تحديث جميع المستهلكين لاستخدام المفتاح الجديد

الخطوة 3: المفتاح القديم يُبطل تلقائياً بعد فترة السماح
```

---

## 7. نظرة عامة على مخطط قاعدة البيانات

تحتوي قاعدة البيانات على **26 جدولاً** منظمة في 4 فئات:

### الجداول الأساسية (8)

| الجدول | الغرض |
|---|---|
| `Users` | حسابات المستخدمين مع الملف الشخصي والحالة والقفل وحقول التدقيق |
| `Applications` | التطبيقات/الخدمات المسجلة التي تستخدم AuthSystem |
| `Roles` | أدوار محددة النطاق للتطبيقات (null = عامة) |
| `Permissions` | صلاحيات هرمية مع دعم أحرف البدل |
| `UserRoles` | تعيينات المستخدم-الدور |
| `RolePermissions` | ربط الدور-الصلاحية |
| `UserPermissions` | منح صلاحيات مباشرة للمستخدم |
| `PermissionImplications` | علاقات الوراثة/التسلسل الهرمي للصلاحيات |

### جداول المصادقة (5)

| الجدول | الغرض |
|---|---|
| `RefreshTokens` | رموز التحديث (مجزأة بـ HMAC-SHA256) مع تتبع التدوير |
| `UserSessions` | الجلسات النشطة مع معلومات الجهاز وتتبع النشاط |
| `LoginAttempts` | تتبع محاولات تسجيل الدخول الفاشلة للقفل |
| `UserExternalLogins` | روابط المزودين الخارجيين (Google، إلخ.) |
| `ExternalAuthProviders` | إعدادات المزودين (Google، Apple، Facebook، إلخ.) |

### جداول المؤسسات (6)

| الجدول | الغرض |
|---|---|
| `Organizations` | سجلات المؤسسات/المستأجرين |
| `OrganizationUsers` | العضوية مع أدوار مستوى المؤسسة |
| `OrganizationInvitations` | دعوات محدودة الوقت مع تتبع الحالة |
| `OrganizationApplications` | اشتراكات التطبيقات لكل مؤسسة |
| `OrganizationUserRoles` | أدوار خاصة بالتطبيق ضمن سياق المؤسسة |
| `OrganizationUserPermissions` | صلاحيات خاصة بالتطبيق ضمن سياق المؤسسة |

### جداول الأمان (7)

| الجدول | الغرض |
|---|---|
| `ApiKeys` | مفاتيح API (مجزأة بـ Argon2id) مع حدود المعدل وقيود IP/المصدر |
| `ApiKeyScopes` | نطاقات الصلاحيات الممنوحة لمفاتيح API |
| `TwoFactorAuth` | أسرار TOTP وأكواد الاسترداد لكل مستخدم |
| `AuditLogs` | سجل تدقيق شامل لجميع العمليات |
| `PasswordHistory` | تجزئات كلمات المرور السابقة لمنع إعادة الاستخدام |
| `EmailVerificationTokens` | رموز التحقق من البريد محدودة الوقت |
| `PasswordResetTokens` | رموز إعادة تعيين كلمة المرور محدودة الوقت |

### الإجراءات المخزنة

منظمة حسب المجال:
- **المصادقة:** `sp_ValidateCredentials`، `sp_CreateRefreshToken`، `sp_ValidateRefreshToken`، `sp_RevokeRefreshToken`، `sp_RevokeAllUserTokens`، `sp_CheckAccountLockout`، `sp_RecordLoginAttempt`
- **المستخدمون:** `sp_GetUserById`، `sp_GetUserByEmail`
- **التفويض، الأدوار، الصلاحيات، التطبيقات، مفاتيح API، التدقيق، المصادقة الثنائية** — إجراءات مخزنة إضافية لكل مجال

---

## 8. أفضل ممارسات الأمان

التدابير الأمنية التالية مطبقة في جميع أنحاء النظام:

### أمان كلمات المرور
- تجزئة **Argon2id** مع معاملات OWASP 2024 الموصى بها (19 ميجابايت ذاكرة، 2 تكرار، 1 خيط)، مع ملح فريد لكل كلمة مرور، ومقارنة ثابتة الوقت، وإعادة تجزئة عند تسجيل الدخول
- كلمات مرور بحد أدنى 8 أحرف (قابل للتكوين؛ OWASP توصي بـ 12+) مع متطلبات التعقيد (أحرف كبيرة، صغيرة، رقم، حرف خاص)
- تتبع سجل كلمات المرور (يمنع إعادة استخدام كلمات المرور الأخيرة)
- انتهاء صلاحية كلمة المرور (قابل للتكوين، معطّل افتراضياً)
- قفل الحساب بعد 5 محاولات فاشلة (قفل 15 دقيقة)
- **فلفل اختياري (Pepper)** — سر من جانب الخادم يُمزج في كل تجزئة ويُخزَّن في مخزن الأسرار (وليس قاعدة البيانات) للدفاع ضد اختراق قاعدة البيانات وحدها
- **فحص اختياري لكلمات المرور المخترقة** — يرفض أو يحذّر من كلمات المرور الموجودة في HIBP Pwned Passwords عبر واجهة النطاق بلا مفتاح وبخاصية k-anonymity

### أمان الرموز
- **توقيع JWT غير متماثل RS256** — الخدمات الخارجية تتحقق من الرموز دون معرفة المفتاح الخاص
- رموز وصول قصيرة العمر (15 دقيقة) تقلل نافذة الاختراق
- تدوير رموز التحديث — كل تحديث يولد رمز تحديث جديد، القديم يُبطل
- القائمة السوداء لـ JWT — رموز الوصول المُبطلة تُرفض فوراً عبر البرمجيات الوسيطة
- رموز التحديث تُخزن كتجزئات HMAC-SHA256 (ليس بالنص العادي أبداً)
- مفاتيح API تُخزن كتجزئات Argon2id (ليس بالنص العادي أبداً)

### التشفير عند الراحة
- **تخزين أسرار قابل للتبديل** (`SecretManagement:StorageMode`): `PlainText` أو `Certificate` أو `Dpapi`
  - **Certificate** — الأسرار مشفّرة في `secrets.dpapi`، محمية بشهادة X.509 تملكها؛ محمولة بين الخوادم (موصى بها للاستضافة المشتركة)
  - **Dpapi** — تشفير Windows مرتبط بالجهاز؛ الأسرار لا تُفك إلا على نفس الجهاز + الحساب
  - **PlainText** — المفاتيح في `appsettings.Production.json`، محمية بصلاحيات الملف فقط (أحكِم قفل الملف)
- ملف الأسرار وحلقة مفاتيح حماية البيانات مخزّنان خارج جذر الويب العام
- انسخ مخزن الأسرار احتياطياً (وملف `.pfx` في وضع Certificate) — فقدانه يُبطل كل الرموز، والفلفل الاختياري إن فُعِّل يقفل المستخدمين المُفلفَلين نهائياً

### أمان النقل
- HTTPS مفروض في الإنتاج عبر HSTS (365 يوماً، includeSubDomains، preload)
- تشفير TLS لاتصالات SQL Server

### أمان الطلبات
- **التحقق من رمز البوابة** مع مقارنة ثابتة الوقت (يمنع هجمات التوقيت)
- **تحديد المعدل** على مستوى البوابة والـ API (نقاط نهاية المصادقة: 5 طلبات/60 ثانية)
- **CORS** مع قائمة بيضاء صريحة للأصول في الإنتاج
- **رؤوس أمان OWASP**: X-Frame-Options (DENY)، X-Content-Type-Options (nosniff)، CSP، Referrer-Policy، Permissions-Policy
- إزالة رؤوس Server وX-Powered-By

### التفويض
- التحكم بالوصول المبني على الصلاحيات (وليس فقط المبني على الأدوار)
- مطابقة صلاحيات بأحرف البدل للوصول الهرمي
- تضمينات الصلاحيات للوراثة
- تفويض مبني على مطالبات JWT (لا استعلام قاعدة بيانات لكل طلب)

### التدقيق والمراقبة
- تسجيل تدقيق شامل لجميع العمليات (من، ماذا، متى، أين)
- تسجيل منظم مع معرفات الارتباط لتتبع الطلبات
- تتبع عنوان IP وUser-Agent
- تتبع القيم القديمة/الجديدة لتدقيق التغييرات

---

## 9. الاختبارات

### حزمة الاختبارات

| الحزمة | الغرض |
|---|---|
| **xUnit** | إطار الاختبار |
| **Moq** | مكتبة المحاكاة |
| **FluentAssertions** | صيغة تأكيدات قابلة للقراءة |
| **coverlet** | جمع تغطية الكود |

### تشغيل الاختبارات

```bash
dotnet test Auth/Auth_API.Tests
```

مع التغطية:

```bash
dotnet test Auth/Auth_API.Tests --collect:"XPlat Code Coverage"
```

### مجموعة Postman

مجموعة Postman كاملة متاحة في:
```
Auth/Auth_API/Postman/AuthSystem.postman_collection.json
```

**الميزات:**
- متغيرات مكوّنة مسبقاً (`baseUrl`، `accessToken`، `refreshToken`، إلخ.)
- سكريبتات اختبار تملأ تلقائياً (استجابة تسجيل الدخول تملأ متغير `accessToken`)
- جميع نقاط النهاية منظمة حسب الوحدة
- الرابط الأساسي: `http://localhost:5000`

للاستخدام: استورد المجموعة في Postman وحدّث متغير `baseUrl` إذا لزم الأمر.

---

## 10. استكشاف الأخطاء وإصلاحها

### أخطاء سلسلة الاتصال

**العَرَض:** `SqlException: Cannot open database "AuthDB"`

**الحل:** تحقق أن `ConnectionStrings:AuthDb` في `appsettings.json` يشير إلى مثيل SQL Server الخاص بك. للتطوير المحلي مع مصادقة Windows:

```json
"Server=.\\SQLEXPRESS;Database=AuthDB;Trusted_Connection=True;TrustServerCertificate=True"
```

### أخطاء الأسرار / حلقة المفاتيح

**العَرَض:** `An error occurred while reading the key ring` / `Access to the path ... is denied`

**السبب:** `DataProtection:KeyPath` فارغ، فتعود حلقة المفاتيح إلى مجلد لا تستطيع هوية تجمّع التطبيقات (app-pool) الكتابة إليه (على IIS / الاستضافة المشتركة تعود إلى مسار تحت `systemprofile`).

**الحل:** اضبط `KeyPath` على مجلد قابل للكتابة خارج جذر الويب العام — **نفس** المجلد لـ Auth API وAPI Gateway — وامنح هوية تجمّع التطبيقات صلاحية *Modify* عليه.

**العَرَض (وضع Dpapi):** `CryptographicException` / "Failed to decrypt … different machine"

**الأسباب:** التشغيل على نظام غير Windows (`Dpapi`/`Certificate` لـ Windows فقط)، أو أن ملف الأسرار أُنشئ على جهاز مختلف أو بحساب مختلف، أو حلقة المفاتيح مفقودة.

**الحل:** أبقِ Auth API والبوابة على جهاز واحد يتشاركان حلقة مفاتيح واحدة، أو استخدم وضع **Certificate** للقابلية للنقل. إذا اضطررت للنقل، ورّد مفاتيحك الخاصة (BYOK) وأعد استيرادها على الخادم الجديد.

### عدم تطابق رمز البوابة

**العَرَض:** `403 Forbidden` على جميع الطلبات عبر البوابة

**الحل:** يجب أن يتفق Auth API وAPI Gateway على رمز البوابة وأن يستخدما **نفس** `StorageMode`:
1. **PlainText** — انسخ `Gateway:ExpectedToken` المولّد في الـ API إلى `Gateway:Token` في البوابة.
2. **Certificate/Dpapi** — شغّل كليهما على نفس الجهاز ووجّههما إلى **نفس** حلقة المفاتيح وملف `secrets.dpapi`؛ عندها يقرأ كلاهما الرمز تلقائياً. أعد تشغيل كليهما بعد أي إعادة توليد للأسرار.

> للاطلاع على مصفوفة استكشاف أخطاء الإنتاج الكاملة (أخطاء بدء التشغيل 500.30، ومشاكل كلمة مرور الشهادة، ومسح المفاتيح عند إعادة النشر)، راجع [PRODUCTION_DEPLOYMENT_GUIDE.md §D](PRODUCTION_DEPLOYMENT_GUIDE.md).

### أخطاء CORS

**العَرَض:** طلبات المتصفح تفشل بأخطاء سياسة CORS

**الحل:**
- التطوير: تأكد أن `appsettings.Development.json` يحتوي على `"AllowedOrigins": ["*"]`
- الإنتاج: أضف أصل الواجهة الأمامية صراحة إلى `Cors:AllowedOrigins`

### دورة تسجيل الدخول تدور بين ‎/login و‎/authorize

**العَرَض:** بيانات الاعتماد تُقبل، ومع ذلك يعيد `/auth/authorize` التوجيه فورًا إلى صفحة الدخول — بلا خطأ في الطرفية ولا في سجلّ الخادم.

**السبب الأول — اختلاف المخطَّط (scheme).** كوكي جلسة الـIdP يُكتب بـ`SameSite=Lax; Secure` على استجابة نداء `fetch` هو `POST /auth/login`. وقاعدة Chrome المسمّاة schemeful same-site تَعُدّ `http://localhost` و`https://localhost` موقعين مختلفين، فتصبح تلك الاستجابة — من واجهة تعمل على http — مورِدًا فرعيًا عابرًا للمواقع، فيُهمَل الكوكي بدل تخزينه. وبلا الكوكي لا توجد جلسة دخول موحّد تُستأنف. الحل: تشغيل الواجهتين على https — راجع `Auth_UI/README.md` › *Dev TLS* — وتشغيل الـAPI على profile الـ`https`.

**السبب الثاني — اختلاف الأصل (origin).** يجب أن يساوي `IdentityProvider:PublicBaseUrl` الأصلَ الذي بُنيت عليه الواجهة (`VITE_API_BASE_URL`). فنقطة authorize تبني `returnTo` من `PublicBaseUrl`، بينما ترفض `getValidReturnTo` في `packages/auth` أي أصل آخر بوصفه محاولة إعادة توجيه مفتوحة، فتُسقط استئناف ما بعد الدخول بصمت. تحقّق من قيمة `returnTo` في شريط العنوان على صفحة الدخول: يجب أن يطابق أصلُها `VITE_API_BASE_URL` حرفًا بحرف.

**إن لم يُحدث تعديل `appsettings.Development.json` أي أثر**، فثمة طبقة لاحقة في سلسلة الإعداد تعلو عليه. بترتيب الأسبقية:

1. `appsettings.Development.local.json` — مُتجاهَل في git، وعلى أغلب الأجهزة يحمل نسخته الخاصة من أقسام `IdentityProvider` و`Email` و`ImageStorage` منقولةً عن إعداد أقدم. **ابدأ التحقّق من هنا**؛ فالملف المتعقَّب في git لا يستطيع تجاوزه.
2. إعدادات النظام المدعومة بقاعدة البيانات (Settings → Access في الـconsole) — وهي تعلو على الملفين معًا. شغّل الـAPI بـ`AUTH_DISABLE_DB_SETTINGS=true` لتخطّي الطبقة، أو صحّح القيمة من الـconsole.

### تعارض المنافذ

**المنافذ الافتراضية:**
- Auth_API: `http://localhost:5100`، `https://localhost:5101`
- API_Gateway: `http://localhost:5034`، `https://localhost:7159`
- واجهة console: `https://localhost:5173` — واجهة accounts: `https://localhost:5174`
  (كلاهما مثبَّت بـ`strictPort`، وكلاهما مدرَج في `Cors:AllowedOrigins`)

إذا كانت المنافذ مستخدمة، عدّل `Properties/launchSettings.json` في المشروع المعني، أو `server.port` في ملف `vite.config.ts` الخاص بالتطبيق.

### رأس انتهاء صلاحية رمز JWT

عندما ينتهي رمز JWT، يعيد الـ API رأس `Token-Expired: true` مع استجابة 401. استخدم هذا لتفعيل تدفق تحديث الرمز في عميلك.

### خدمة البريد لا ترسل

تأكد أن `Email:Enabled` مضبوط على `true` وبيانات اعتماد SMTP مكوّنة. أبقِ كلمة مرور SMTP خارج `appsettings.json` — عيّنها عبر متغير البيئة `Email__Password`، أو خزّنها في مخزن الأسرار المشفّر (وضعا Certificate/Dpapi).

---

## 11. مصفوفة الصلاحيات

القائمة الكاملة لجميع الصلاحيات المستخدمة عبر النظام:

| كود الصلاحية | المتحكم | الإجراءات |
|---|---|---|
| `users:read` | UsersController | عرض المستخدمين، الحصول على مستخدم، أدوار/صلاحيات المستخدم |
| `users:create` | UsersController | إنشاء مستخدم |
| `users:update` | UsersController | تحديث ملف المستخدم |
| `users:delete` | UsersController | حذف مستخدم |
| `users:manage` | UsersController | قفل، فتح، تفعيل، تعطيل الحسابات |
| `users:manage-roles` | UsersController | تعيين وإزالة أدوار المستخدم |
| `users:manage-permissions` | UsersController | منح وسحب صلاحيات المستخدم |
| `roles:read` | RolesController | عرض والحصول على الأدوار |
| `roles:create` | RolesController | إنشاء أدوار |
| `roles:update` | RolesController | تحديث أدوار |
| `roles:delete` | RolesController | حذف أدوار |
| `permissions:read` | PermissionsController | عرض والحصول على الصلاحيات، عرض التضمينات |
| `permissions:create` | PermissionsController | إنشاء صلاحيات |
| `permissions:update` | PermissionsController | تحديث صلاحيات |
| `permissions:delete` | PermissionsController | حذف صلاحيات |
| `permissions:manage` | PermissionsController | إضافة وإزالة تضمينات الصلاحيات |
| `applications:read` | ApplicationsController | عرض التطبيقات، تفاصيل التطبيق، الأدوار، الصلاحيات |
| `applications:create` | ApplicationsController | إنشاء تطبيقات |
| `applications:update` | ApplicationsController | تحديث تطبيقات |
| `applications:delete` | ApplicationsController | حذف تطبيقات |
| `apikeys:read` | ApiKeysController | عرض مفاتيح API |
| `apikeys:create` | ApiKeysController | إنشاء مفاتيح API |
| `apikeys:revoke` | ApiKeysController | إبطال مفاتيح API |
| `apikeys:rotate` | ApiKeysController | تدوير مفاتيح API |
| `auditlogs:read` | AuditLogsController | استعلام وعرض سجلات التدقيق |
| `auditlogs:export` | AuditLogsController | تصدير سجلات التدقيق إلى ملف |
| `org:update` | OrganizationsController | تحديث تفاصيل المؤسسة |
| `org:members:read` | OrganizationsController | عرض الأعضاء والدعوات |
| `org:members:manage` | OrganizationsController | تحديث أدوار الأعضاء، إزالة الأعضاء |
| `org:members:invite` | OrganizationsController | إرسال دعوات المؤسسة |
| `org:apps:read` | OrganizationsController | عرض تطبيقات المؤسسة |
| `org:apps:manage` | OrganizationsController | تفعيل، تحديث، تعطيل تطبيقات المؤسسة |
| `org:permissions:manage` | OrganizationsController | تعيين أدوار التطبيق ومنح الصلاحيات للأعضاء |
| `secrets.manage` | SecretsController | عرض الحالة، توليد واستيراد المفاتيح (BYOK)، إدارة الأسرار المخصصة |

---

*يغطي هذا الدليل AuthSystem v1.0 العامل على NET 10. للتحديثات، راجع سجل تغييرات المستودع ومجموعة Postman.*

</div>
