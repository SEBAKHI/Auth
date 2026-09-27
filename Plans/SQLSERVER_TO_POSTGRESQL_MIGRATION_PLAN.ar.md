# خطة ترحيل `SEBAKHI/Auth` من SQL Server إلى PostgreSQL

## 0. Decision Summary

1. **Common Core (المسارَان):** إبقاء Dapper 2.1.79 والمستودعات وواجهاتها؛ استبدال `Microsoft.Data.SqlClient` بـ `Npgsql`؛ إعادة كتابة T-SQL في 46 ملف Persistence آليًّا حيث أمكن (quoting، `TOP`/`OFFSET`، `GETUTCDATE()`، BIT)، ويدويًّا في 9 مواضع قفل/upsert؛ استبدال SSDT/DACPAC بمشروع DbUp (`Auth_DB.csproj`) يحمل baseline واحدًا + seed يعمل في كل نشر.
2. **Track A (إنتاج حيّ):** cutover دفعة واحدة داخل نافذة صيانة (API يردّ 503) بنسخ البيانات عبر أداة .NET داخلية (`SqlDataReader` → Npgsql binary `COPY`) مع checksum مُطبَّع على الجانبين. لا dual-write ولا CDC إلا إذا ثبت من التدريبين أن مدة النسخ تتجاوز الميزانية (§8.1).
3. **Track B (لم يُنشر):** Common Core + حذف كل أثر SQL Server (sqlproj، procs، Upgrades، الحزم، الوثائق) وإعادة ضبط بيئة المطوّر.
4. **الجهد التقديري:** Common Core 18–26 يوم-مطوّر؛ Track A +9–13 (أداة النسخ، تدريبان، runbook)؛ Track B +2–3. الافتراض: مطوّر واحد يعرف المستودع، وحجم بيانات ≤ 50 GB [UNVERIFIED].
5. **أعلى ثلاثة مخاطر:** (R-01) الانتقال من collation CI إلى مقارنة حسّاسة للحالة يكسر 52 موضع مساواة و31 `LIKE` بصمت؛ (R-02) قيود UNIQUE ذات أعمدة NULL تسمح بصفوف مكرّرة في PostgreSQL ما لم تُعرَّف `NULLS NOT DISTINCT`؛ (R-03) `DateTime.Kind` مع Npgsql يرمي استثناءً عند كل كتابة `timestamptz` لقيمة `Unspecified` قُرئت من القاعدة.
6. **قرارات المالك المطلوبة قبل البدء:** (Q-01) المسار A أم B؟ (Q-02) حجم كل جدول وأقصى downtime مقبول؛ (Q-03) قبول الانحرافات الموثّقة D-07 وD-11 وD-15 وD-19؛ (Q-04) استضافة PostgreSQL (Windows بجوار IIS أم Linux) ونسخته؛ (Q-05) معامل E5 (الافتراضي 1.2).

## 1. Scope & Non-Goals

الحالة المتحقَّق منها عند `f80ba19` (branch `main`) بتاريخ 2026-09-27؛ كل ادّعاء عن المستودع يحمل `path:line` فُتح في هذه الجلسة، وكل عدد يحمل الأمر الذي أنتجه، وما لم يُتحقَّق منه موسوم `[UNVERIFIED]` ومجموع في §12.

**في النطاق:** كل ما يربط المستودع بـ SQL Server: المشروع `Auth/Auth_DB` (54 جدولًا، 9 procedures، 11 Upgrade، 16 SeedData، PostDeployment)، طبقة `Auth/Auth.Infrastructure/Persistence/` (46 ملفًا، 12,850 سطرًا: `ls Auth/Auth.Infrastructure/Persistence/*.cs | wc -l` → 46؛ `cat … | wc -l` → 12850)، أربعة مواضع SQL خارجها، ترجمة الأخطاء في `Auth_API`، health check، التهيئة، الاختبارات (247 ملفًا / 2000 `[Fact|Theory]`: `find Auth/Auth_API.Tests -name "*.cs" | wc -l` → 247؛ `grep -rE "\[Fact|\[Theory" Auth/Auth_API.Tests --include=*.cs | wc -l` → 2000)، CI، الوثائق، ونصوص الواجهة التي تذكر SQL Server.

**خارج النطاق (Non-Goals):** أي تغيير في عقد الـAPI أو في `docs/api/error-codes.json` (468 كودًا: `python3 -c "import json;print(len(json.load(open('docs/api/error-codes.json'))['codes']))"` → 468)؛ إدخال ORM؛ إعادة هيكلة الطبقات؛ تحسين أداء يتجاوز E5؛ تغيير طريقة نشر الأحداث (المرحلة الأولى في `.claude/CLAUDE.md`)؛ `API_Gateway` (لا يملك قاعدة بيانات: `grep -n "ConnectionString\|SqlServer" Auth/API_Gateway/Program.cs` → لا شيء؛ الفحص الصحّي فيه `AddUrlGroup` فقط `Auth/API_Gateway/Program.cs:419`).

## 2. Track Selector

| السؤال | القاعدة | كيف يُجاب |
|---|---|---|
| هل توجد بيانات إنتاج يجب حفظها؟ | نعم → Track A؛ لا → Track B. المعيار الوحيد هو **وجود صفوف كتبها عملاء** في أي جدول من الـ54، لا وجود خادم | `SELECT t.name, SUM(p.rows) FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) GROUP BY t.name` على قاعدة الإنتاج |
| الحجم لكل جدول (rows / GB) | يُقاس، لا يُقدَّر. **[UNVERIFIED]** — لا يحتوي المستودع على أي رقم حجم | `EXEC sp_spaceused N'dbo.AuditLogs'` لكل جدول، أو `sys.dm_db_partition_stats`. الجداول المرشّحة للحجم الأكبر بحكم شكلها append-only: `AuditLogs` (`Auth/Auth_DB/dbo/Tables/Security/AuditLogs.sql`)، `LoginAttempts`، `RefreshTokens`، `UserSessions`، `NotificationOutbox` |
| أقصى downtime مقبول | **[UNVERIFIED]** — يقرّره المالك. الخطة تُخرج المدة كدالّة في القياس (§8.1) وتقارنها بهذا الرقم | Q-02 |
| وجود نسخة `.bak` حديثة يمكن استعادتها في staging | شرط للتدريب (§8.4). الوثائق تذكر نسخًا يومية (`ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md:1334-1341`) لكن لا نصّ backup في المستودع | Q-02 |

## 3. Verified Current State

| البند | الحقيقة | الأمر / المصدر |
|---|---|---|
| المخطط | SSDT، `Sql160DatabaseSchemaProvider`، `ModelCollation 1033, CI` | `Auth/Auth_DB/Auth_DB.sqlproj:10,16` |
| الجداول | 54 (الوثائق تقول 52) | `grep -rl "CREATE TABLE" Auth/Auth_DB/dbo/Tables \| wc -l` → 54؛ `README.md:52` |
| الفهارس | 136 (5 UNIQUE، 74 filtered، 7 INCLUDE، 27 DESC) | `grep -rniE "CREATE\s+(UNIQUE\s+)?(NONCLUSTERED\s+)?INDEX" Auth/Auth_DB/dbo/Tables \| wc -l` → 136؛ `grep -rniE "^\s*WHERE\b\|\)\s*WHERE\b" … \| wc -l` → 74؛ `grep -rniE "INCLUDE\s*\(" … \| wc -l` → 7 |
| القيود | 75 FK (11 `ON DELETE CASCADE`)، 33 UNIQUE، 15 CHECK (5 منها `ISJSON`) | `grep -rniE "CONSTRAINT\s+\[FK_" … \| wc -l` → 75؛ `grep -rni "ON DELETE CASCADE" … \| wc -l` → 12 (واحد تعليق `Auth/Auth_DB/dbo/Tables/Core/ApplicationUserAccess.sql:40`)؛ `grep -rniE "CONSTRAINT\s+\[CK_" … \| wc -l` → 15 |
| الأنواع | 190 `UNIQUEIDENTIFIER`، 144 `DATETIME2` بلا دقّة، 30 `BIT`، 149 `NVARCHAR(n)`، 20 `NVARCHAR(MAX)`، 3 `CHAR(64)`، 12 `TINYINT`، 22 `INT`، 1 `BIGINT`، 1 `ROWVERSION`؛ **صفر** `VARBINARY`/`IDENTITY`/`SEQUENCE`/`XML`/views/triggers/functions | `grep -rniE "^\s*\[\w+\]\s+UNIQUEIDENTIFIER" Auth/Auth_DB/dbo/Tables \| wc -l` → 190؛ `… DATETIME2 …` → 144؛ `grep -rniE "\]\s+BIT\b" … \| wc -l` → 30؛ `grep -rn -F "VARBINARY(" Auth/Auth_DB/dbo/Tables \| wc -l` → 0؛ `grep -rn -F "SEQUENCE" … \| wc -l` → 0 |
| Procedures | 9 ملفات؛ 4 مُستدعاة من C#، 5 ميتة | `ls -R Auth/Auth_DB/dbo/StoredProcedures`؛ لكل اسم: `grep -rn "<name>" Auth --include=*.cs \| grep -v Tests \| wc -l` → `sp_GetUserById` 1، `sp_GetUserByEmail` 1، `sp_CreateRefreshToken` 1، `sp_RevokeAllUserTokens` 1، الباقي 0 |
| Upgrades/Seed | 11 Upgrade مؤرَّخ + 2 نص فضفاض + نسخة sqlproj قديمة؛ 16 SeedData؛ PostDeployment بـ18 `:r` و47 `IF NOT EXISTS` | `ls Auth/Auth_DB/dbo/Scripts/Upgrades \| wc -l` → 11؛ `ls Auth/Auth_DB/dbo/Scripts/*.sql` → `ALTER_Users_PasswordHash_Nullable.sql`, `RECONCILE_prod_constraint_names.sql`؛ `grep -c "^:r" …/Script.PostDeployment.sql` → 18 |
| الوصول للبيانات | Dapper 2.1.79 فوق `Microsoft.Data.SqlClient` 7.0.2؛ 418 استدعاء Dapper في 43 ملفًا؛ لا TypeHandler ولا TVP ولا SqlBulkCopy | `Auth/Auth.Infrastructure/Auth.Infrastructure.csproj:10,15`؛ `grep -rn "AddTypeHandler\|SqlMapper\.\|AsTableValuedParameter" Auth --include=*.cs` → 0 |
| T-SQL في C# | 635 `[dbo].[` في 41 ملف Persistence؛ 150 `GETUTCDATE()` في 25؛ 18 `SELECT TOP`؛ 11 `UPDATE/DELETE TOP`؛ 12 `OFFSET … FETCH`؛ 4 `MERGE`؛ 5 `UPDLOCK, HOLDLOCK`؛ 1 `READPAST`؛ 4 `OUTPUT`؛ 199 مقارنة BIT بـ0/1؛ 34 `ISNULL`؛ 17 `DATEADD`؛ 8 `AT TIME ZONE`؛ 3 `STRING_AGG WITHIN GROUP`؛ 4 `EXEC` | `grep -rnE "\[dbo\]\.\[" Auth/Auth.Infrastructure/Persistence \| wc -l` → 635؛ `grep -rn -F "GETUTCDATE()" … \| wc -l` → 150؛ `grep -rn "SELECT TOP" … \| wc -l` → 18؛ `grep -rn "DELETE TOP\|UPDATE TOP" … \| wc -l` → 11؛ `grep -rn "OFFSET " … \| wc -l` → 12؛ `grep -rnE "\[Is\w+\] = [01]\b" … \| wc -l` → 195 (+4 بمعاملات `@IncludeDeleted = 1`) |
| ترجمة الأخطاء | مترجم واحد لـ`SqlException` (547 → `Persistence.ReferenceConflict`؛ 9 أرقام → outage 503؛ الباقي 500)؛ 6 مواضع `2601 or 2627`؛ 3 مواضع `1205` | `Auth/Auth_API/Common/Errors/SqlExceptionProblemTranslator.cs:16-37`؛ `grep -rn "\.Number is" Auth --include=*.cs \| grep -v Tests \| wc -l` → 9 |
| الوقت | القاعدة تُعيد `Kind.Unspecified` ويُصلحه محوِّل JSON فقط؛ لا `SpecifyKind` في المستودعات | `Auth/Auth_API/Program.cs:725-728`؛ `Auth/Auth_API/Common/UtcDateTimeConverter.cs:29-31` |
| الاختبارات | **لا اختبار يفتح اتصالًا بقاعدة** («the test project has no database»)؛ 12 ملفًا تُثبت نصّ T-SQL بالـregex؛ 9 تقرأ ملفات SSDT؛ `SqlException` يُبنى بالانعكاس | `Auth/Auth_API.Tests/Infrastructure/SessionPersistenceSqlTests.cs:13-16`؛ `grep -rlE "\[dbo\]\|HOLDLOCK\|GETUTCDATE\|OFFSET @Offset\|SELECT TOP\|MERGE" Auth/Auth_API.Tests --include=*.cs \| wc -l` → 12؛ `Auth/Auth_API.Tests/Helpers/SqlExceptions.cs:17-19` |
| CI | `windows-latest`، بلا قاعدة ولا sqlpackage ولا docker | `.github/workflows/ci.yml:45,69` |
| النشر | IIS + DACPAC (`SqlPackage /Action:Publish`)، قاعدة تُنشر يدويًّا من Visual Studio | `ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md:313-360`؛ `Auth/Auth_API/Common/HealthChecks/DatabaseReadinessHealthCheck.cs:46-53` |
| RCSI | غير مذكور في أي ملف؛ لا `IsolationLevel` صريح إلا SERIALIZABLE داخل T-SQL واحد | `grep -rn -i "READ_COMMITTED_SNAPSHOT\|RCSI" Auth ReadMe docs README.md` → 0؛ `Auth/Auth.Infrastructure/Persistence/RoleRepository.cs:266` |
| سلسلة الاتصال | `AuthDb`؛ placeholder في `appsettings.json`؛ تطوير: `Data Source=localhost\SQLEXPRESS01;…;TrustServerCertificate=True;MultipleActiveResultSets=true`؛ الإنتاج من DPAPI secret `ConnectionStrings.AuthDb` | `Auth/Auth_API/Program.cs:337`؛ `Auth/Auth_API/appsettings.json:14`؛ `Auth/Auth_API/appsettings.Development.json:19`؛ `Auth/Auth.Shared/Configuration/DpapiSecretConfigurationProvider.cs:183` |

## 4. Coupling Inventory

L = احتمال أن يكسر الترحيلَ إن أُهمل (1–5)، I = الأثر (1–5). **اصطلاح:** أي ملف `*.cs` يُذكر باسمه وحده (مثل `UserRepository.cs:277`) يقع تحت `Auth/Auth.Infrastructure/Persistence/`.

| INV | path:line | construct | category | PG target | L | I | L×I |
|---|---|---|---|---|---|---|---|
| INV-01 | `Auth/Auth_DB/Auth_DB.sqlproj:10,16,144-152,188` | SSDT project، DSP Sql160، `ModelCollation 1033, CI`، PostDeploy | DDL/ops | مشروع DbUp `Auth_DB.csproj` (D-14) | 5 | 5 | 25 |
| INV-02 | `Auth/Auth_DB/dbo/Tables/*/*.sql` (54 ملفًا؛ 933 `[dbo].[` عبر كل `.sql`: `grep -rno "\[dbo\]\.\[" Auth/Auth_DB \| wc -l`) | `CREATE TABLE [dbo].[X]`، bracket quoting، schema `dbo` | DDL | `CREATE TABLE "X"` في `public`، quoted PascalCase (D-04) | 5 | 5 | 25 |
| INV-03 | 190 عمود `UNIQUEIDENTIFIER`، 52 `DEFAULT NEWID()` (`grep -rni "NEWID()" Auth/Auth_DB/dbo/Tables \| wc -l` → 52)؛ نموذج `Auth/Auth_DB/dbo/Tables/Core/Users.sql:3` | GUID PK/FK | type | `uuid DEFAULT gen_random_uuid()` (D-05) | 5 | 4 | 20 |
| INV-04 | 144 عمود `DATETIME2` بلا دقّة، 61 `DEFAULT GETUTCDATE()`؛ نموذج `Auth/Auth_DB/dbo/Tables/Core/Users.sql:36` | datetime2(7) UTC بلا offset | type | `timestamptz DEFAULT now()` بدقّة 6 (D-06) | 5 | 4 | 20 |
| INV-05 | 30 عمود `BIT` في 18 ملفًا؛ نموذج `Auth/Auth_DB/dbo/Tables/Core/Users.sql:23-25,32,40` | BIT + defaults 0/1 + فهارس `WHERE [IsDeleted] = 0` (`Auth/Auth_DB/dbo/Tables/Core/Users.sql:61`) | type | `boolean DEFAULT false`، partial index `WHERE NOT "IsDeleted"` (D-08) | 5 | 4 | 20 |
| INV-06 | 149 `NVARCHAR(n)`، 20 `NVARCHAR(MAX)` (قائمة: `grep -rn "NVARCHAR(MAX)" Auth/Auth_DB/dbo/Tables`)، 3 `CHAR(64)` (`Auth/Auth_DB/dbo/Tables/Authentication/UserSessions.sql:18`) | نصوص UTF-16 تحت collation CI | type/semantics | `varchar(n)` / `text` / `char(64)`؛ أعمدة المفاتيح النصية → `citext` (D-05, D-07) | 5 | 5 | 25 |
| INV-07 | 12 `TINYINT` (`Auth/Auth_DB/dbo/Tables/Core/Users.sql:26`)، 22 `INT`، 1 `BIGINT` (`Auth/Auth_DB/dbo/Tables/Security/UploadedImages.sql:5`) | أعداد صحيحة | type | `smallint` / `integer` / `bigint` (D-05) | 3 | 3 | 9 |
| INV-08 | `Auth/Auth_DB/dbo/Tables/System/SystemSettingsOverrides.sql:8`؛ `Auth/Auth.Infrastructure/Persistence/SystemSettingsRepository.cs:62-90`؛ `Auth/Auth.Application/Features/SystemSettings/UpdateSystemSettings/UpdateSystemSettingsCommandHandler.cs:107-143` | `ROWVERSION` + `WHERE [RowVersion] = @ExpectedRowVersion` + base64 على السلك | type/DML | `bigint` عدّاد يزداد في الـUPDATE، يُسلسَل 8 بايت big-endian base64 (D-11) | 5 | 4 | 20 |
| INV-09 | `Auth/Auth_DB/dbo/Tables/Core/Users.sql:17` | `[FullName] AS (ISNULL(…)+N' '+ISNULL(…)) PERSISTED` | DDL | `GENERATED ALWAYS AS (coalesce("FirstName",'')\|\|' '\|\|coalesce("LastName",'')) STORED` (D-05) | 4 | 3 | 12 |
| INV-10 | 74 filtered index (`Auth/Auth_DB/dbo/Tables/Core/Users.sql:59-80`، `Auth/Auth_DB/dbo/Tables/Security/PendingRegistrations.sql:54-56`)، 7 `INCLUDE` (`Auth/Auth_DB/dbo/Tables/Security/AuditLogs.sql:82`)، 27 `DESC` | فهارس مُرشَّحة/تغطية | DDL | partial indexes، `INCLUDE`، `DESC` مدعومة (D-05) | 3 | 3 | 9 |
| INV-11 | 75 FK، 11 `ON DELETE CASCADE` (`Auth/Auth_DB/dbo/Tables/Organizations/OrganizationUsers.sql:18`) | referential actions | DDL | نفسها | 1 | 3 | 3 |
| INV-12 | 15 CHECK؛ 5 `ISJSON(...) = 1` (`Auth/Auth_DB/dbo/Tables/Notifications/NotificationTypes.sql:22-23`، `Auth/Auth_DB/dbo/Tables/System/SystemSettingsOverrides.sql:10`)؛ `Auth/Auth_DB/dbo/Tables/Core/Users.sql:47` | CHECK بدوال T-SQL | DDL | `CHECK ("X" IS JSON)` (PG ≥ 16)؛ الباقي كما هو (D-02, D-05) | 4 | 3 | 12 |
| INV-13 | 33 UNIQUE؛ 5 منها بعمود NULL: `Auth/Auth_DB/dbo/Tables/Core/Roles.sql:17`، `Auth/Auth_DB/dbo/Tables/Core/UserPermissions.sql:16`، `Auth/Auth_DB/dbo/Tables/Core/UserRoles.sql:16`، `Auth/Auth_DB/dbo/Tables/Notifications/NotificationLayouts.sql:31`، `Auth/Auth_DB/dbo/Tables/Notifications/NotificationTemplates.sql:28` (التعليق في `:26-27` يصرّح بالاعتماد على مساواة NULL) | UNIQUE يعامل NULL كقيمة واحدة | DDL/semantics | `UNIQUE NULLS NOT DISTINCT` (PG ≥ 15) (D-09) | 5 | 5 | 25 |
| INV-14 | `Auth/Auth_DB/dbo/Tables/Core/Users.sql:34-35` | `DEFAULT CONVERT(NVARCHAR(100), NEWID())` (uppercase) | DDL | `DEFAULT upper(gen_random_uuid()::text)` (D-05) | 3 | 2 | 6 |
| INV-15 | `grep -rn -F "IDENTITY" Auth/Auth_DB/dbo/Tables \| wc -l` → 0؛ `SEQUENCE` → 0؛ `VARBINARY(` → 0؛ `CREATE TRIGGER\|VIEW\|FUNCTION\|SYSTEM_VERSIONING\|FULLTEXT` → 0 | البنود المفترضة في الـseed (SEQUENCE، VARBINARY(MAX)) | DDL | **مرفوضة بالدليل** — لا عمل | 1 | 1 | 1 |
| INV-16 | `Auth/Auth_DB/dbo/StoredProcedures/{Authentication,Users}/*.sql` (9)؛ المستدعاة: `Auth/Auth.Infrastructure/Persistence/UserRepository.cs:69,166`، `RefreshTokenRepository.cs:51,99` | `EXEC [dbo].[sp_*]`؛ `sp_RevokeAllUserTokens` يستعمل table variable + `OUTPUT … INTO` | DDL/DML | inline SQL في المستودعين؛ حذف الـ5 الميتة (D-13) | 5 | 4 | 20 |
| INV-17 | `Auth/Auth_DB/dbo/Scripts/Upgrades/*.sql` (11)؛ `Auth/Auth_DB/dbo/Scripts/Upgrades/2026-08-04_BoundedIdentifierReservation.sql:34,40-58,81` (XACT_ABORT، `sys.tables/indexes/columns`، RAISERROR)؛ `Auth/Auth_DB/dbo/Scripts/Upgrades/2026-08-08_SessionDeviceColumns.sql:41` (`COL_LENGTH`)؛ `Auth/Auth_DB/dbo/Scripts/RECONCILE_prod_constraint_names.sql:63-117` (`sp_rename`، TRY/CATCH، THROW)؛ `Auth/Auth_DB/dbo/Scripts/ALTER_Users_PasswordHash_Nullable.sql`؛ `Auth_DB.sqlproj_backup` | نصوص ترقية T-SQL يدوية، 7 منها تُضمَّن في كل نشر (`Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql:12-30`) | DDL | تُصفّى في baseline واحد؛ لا تُنقل (D-15) | 4 | 4 | 16 |
| INV-18 | `Auth/Auth_DB/dbo/Scripts/SeedData/*.sql` (16) + `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql` (657 سطرًا؛ 18 `:r`؛ 47 `IF NOT EXISTS`؛ `GO`؛ `PRINT`؛ `DECLARE @SystemUserId` في `:45,94,227,308,383`؛ `INSERT INTO [dbo].[Users]` في `:391,438`) | seed idempotent بنمط `IF NOT EXISTS … INSERT` وسلسلة تخطيطات بريد مرتّبة | DDL | نصوص seed تعمل في كل نشر بنمط `INSERT … SELECT … WHERE NOT EXISTS` (D-16) | 5 | 4 | 20 |
| INV-19 | `Auth/Auth.Infrastructure/Auth.Infrastructure.csproj:15`؛ 8 مواضع `using Microsoft.Data.SqlClient` (`grep -rln "Microsoft.Data.SqlClient" Auth --include=*.cs \| grep -v Tests \| wc -l` → 8) | الحزمة والـusings | ops | حزمة `Npgsql` (D-03) | 5 | 5 | 25 |
| INV-20 | `Auth/Auth.Infrastructure/Persistence/SqlConnectionFactory.cs:22`؛ التسجيل `Auth/Auth_API/Program.cs:356` | `new SqlConnection(cs).OpenAsync` | ops | `NpgsqlConnectionFactory` بنفس `IDbConnectionFactory` (D-03) | 5 | 5 | 25 |
| INV-21 | `Auth/Auth.Infrastructure/Persistence/SqlConnectionStringProbe.cs:23-40`؛ التسجيل `Auth/Auth_API/Program.cs:360`؛ `Auth/Auth.Application/Features/Secrets/SetConnectionString/SetConnectionStringCommandHandler.cs:123` (تعليق) | `SqlConnectionStringBuilder{ConnectTimeout=5}` | ops | `NpgsqlConnectionStringBuilder{Timeout=5}` (D-21) | 4 | 3 | 12 |
| INV-22 | `Auth/Auth.Infrastructure/Configuration/DbSettingsConfigurationProvider.cs:151-155`؛ `Auth/Auth_API/Program.cs:384-392` | مزوّد تهيئة يقرأ `SystemSettingsOverrides` بـ`SqlConnection` قبل DI | ops | `NpgsqlConnection` + quoting (D-03) | 5 | 4 | 20 |
| INV-23 | `Auth/Auth_API/Common/IdentifierKeyRegenerationGuard.cs:53-57`؛ `Auth/Auth_API/Program.cs:350-353` | حارس بدء يعدّ `AccountDeletionTombstones` بـ`SqlConnection` | ops | `NpgsqlConnection` + quoting (D-03) | 4 | 3 | 12 |
| INV-24 | `Auth/Auth_API/Common/HealthChecks/DatabaseReadinessHealthCheck.cs:1,59,64,142-145`؛ `Auth/Auth_API/Program.cs:1081-1085` | `COL_LENGTH('dbo.Users','Username') >= 510`، `OBJECT_ID('dbo.PendingRegistrations','U')`، `SELECT 1` | ops | `to_regclass('public."PendingRegistrations"')`، `information_schema.columns.character_maximum_length >= 255` (D-20) | 5 | 3 | 15 |
| INV-25 | `Auth/Auth_API/Auth_API.csproj:19` | `AspNetCore.HealthChecks.SqlServer` غير مستعملة (`grep -rn "AddSqlServer" Auth --include=*.cs` → 0) | ops | حذف الحزمة (D-20) | 2 | 1 | 2 |
| INV-26 | `Auth/Auth_API/Common/Errors/SqlExceptionProblemTranslator.cs:16-37`؛ التسجيل `Auth/Auth_API/Common/Errors/ApiErrorContractExtensions.cs:25`؛ البحث بالنوع الدقيق `Auth/Auth.Shared/Http/ErrorContract/ErrorContractExceptionHandler.cs:31,77` | 547 → `Persistence.ReferenceConflict`؛ {-2,2,53,233,4060,10053,10054,10060,40613} → outage | error | مترجمان: `PostgresException` (23503 → ReferenceConflict؛ فئة 08، 57P0x، 53300، 3D000 → outage) و`NpgsqlException` (timeout/transient → outage) (D-12) | 5 | 5 | 25 |
| INV-27 | `Auth/Auth.Infrastructure/Persistence/UserUiPreferenceRepository.cs:80`، `UserKnownDeviceRepository.cs:230`، `AccountDeletionRequestRepository.cs:47`، `ApplicationAccessRepository.cs:211`، `UserRepository.cs:342`، `PendingRegistrationRepository.cs:90` | `SqlException … when (ex.Number is 2601 or 2627)` | error | `PostgresException … when (ex.SqlState == "23505")` (D-12) | 5 | 4 | 20 |
| INV-28 | `UserRepository.cs:277`، `PendingRegistrationRepository.cs:99,165` | `ex.Number is 1205` → إعادة محاولة واحدة | error | `ex.SqlState is "40P01" or "40001"` (D-10, D-12) | 5 | 4 | 20 |
| INV-29 | 635 `[dbo].[` في 41 ملفًا تحت `Auth/Auth.Infrastructure/Persistence/` + `Auth/Auth_API/Common/IdentifierKeyRegenerationGuard.cs:57`، `Auth/Auth.Infrastructure/Configuration/DbSettingsConfigurationProvider.cs:155`، `Auth/Auth.Infrastructure/Security/EncryptionMigrationService.cs:61,74,81,93` | quoting بالأقواس المربّعة + schema | DML | `"X"` بلا schema (D-04) — إعادة كتابة آلية | 5 | 5 | 25 |
| INV-30 | 150 `GETUTCDATE()` في 25 ملفًا (أعلاها `OrganizationRepository.cs` 31، `UserRepository.cs` 14) | ساعة الخادم UTC لكل عبارة | DML | `now()` مع `TimeZone=UTC` على الاتصال (D-19) | 5 | 3 | 15 |
| INV-31 | 18 `SELECT TOP` (`LoginAttemptRepository.cs:88,115,135`، `DashboardStatsRepository.cs:64,186,211,454`، `ApplicationRepository.cs:418`)؛ 11 `DELETE/UPDATE TOP` (`RefreshTokenRepository.cs:238,243`، `UserSessionRepository.cs:404`، `PendingRegistrationRepository.cs:273`) | تحديد الصفوف | DML | `LIMIT @n`؛ للحذف/التحديث: `WHERE ctid IN (SELECT ctid … LIMIT @n)` أو `Id IN (…)` (D-05) | 5 | 3 | 15 |
| INV-32 | 12 `OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY` في 8 ملفات (`UserRepository.cs:769`، `AuditLogRepository.cs:224`، …) | ترقيم الصفحات | DML | `LIMIT @PageSize OFFSET @Offset` (D-05) | 5 | 3 | 15 |
| INV-33 | `PlatformSettingsRepository.cs:41` (بلا HOLDLOCK)؛ `AccountDeletionTombstoneRepository.cs:28`، `PrivacyPolicyVersionRepository.cs:165`، `UserRepository.cs:567` (`MERGE … WITH (HOLDLOCK)`) | upsert ذرّي | DML/concurrency | `INSERT … ON CONFLICT (…) DO UPDATE` (D-10) | 5 | 4 | 20 |
| INV-34 | `RoleRepository.cs:270`؛ `UserRepository.cs:304,535`؛ `PendingRegistrationRepository.cs:67,192` (التعليق `:61-64` يعتمد على key-range lock لصفّ غائب) | `WITH (UPDLOCK, HOLDLOCK)` | concurrency | `pg_advisory_xact_lock(hashtextextended(@key,0))` قبل `SELECT … FOR UPDATE` داخل نفس المعاملة (D-10) | 5 | 5 | 25 |
| INV-35 | `RoleRepository.cs:264-295` | batch: `SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRAN; TRY… UPDATE WITH (UPDLOCK,HOLDLOCK) … IF @@ROWCOUNT = 0 INSERT … THROW` | concurrency | عبارة واحدة `INSERT … ON CONFLICT ON CONSTRAINT "UQ_UserRoles" DO UPDATE SET …` (تعتمد على INV-13) (D-10) | 5 | 4 | 20 |
| INV-36 | `SystemSettingsRepository.cs:62-90` (`IF EXISTS … ELSE … IF @@ROWCOUNT = 0 … CAST(NULL AS VARBINARY(8))`) | batch إجرائي لتحديث متفائل | DML | `UPDATE … WHERE "RowVersion" = @Expected RETURNING …` ثم `INSERT … ON CONFLICT DO NOTHING RETURNING` في C# (D-11) | 5 | 4 | 20 |
| INV-37 | `NotificationOutboxRepository.cs:80-83` | `UPDATE TOP (@BatchSize) … WITH (ROWLOCK, READPAST) … OUTPUT inserted.*` | concurrency | `WITH c AS (SELECT "Id" FROM … WHERE … ORDER BY "NextAttemptAt" LIMIT @BatchSize FOR UPDATE SKIP LOCKED) UPDATE … FROM c … RETURNING *` (D-10) | 5 | 4 | 20 |
| INV-38 | `AuthorizationCodeRepository.cs:60-62`؛ `UserSessionRepository.cs:56-73,291`؛ `UploadedImageRepository.cs:75` | `OUTPUT INSERTED.*` / `OUTPUT DELETED.[StorageKey]` | DML | `RETURNING` (D-05) | 5 | 3 | 15 |
| INV-39 | 195 سطر `[IsX] = 0/1` في 14 ملفًا (`[IsActive] = 1` 141، `[IsDeleted] = 0` 32)؛ `@IncludeDeleted = 1` (`UserRepository.cs:747,766`)، `@IsPublished = 1/0` (`NotificationTemplateRepository.cs:312-313`)؛ 59 `THEN 1 ELSE 0`؛ 8 `CAST(… AS BIT)` (`UserSessionRepository.cs:49,73`، `RoleRepository.cs:408-409,454-455`، `PermissionRepository.cs:671-673`، `ApplicationAccessRepository.cs:86`) | مقارنات ومشاريع BIT بأعداد | DML | `= TRUE/FALSE` أو `NOT "IsDeleted"`؛ `CASE … THEN TRUE ELSE FALSE END`؛ معاملات bool تُمرَّر كـboolean (D-08) | 5 | 5 | 25 |
| INV-40 | 34 `ISNULL(` (`DashboardStatsRepository.cs:37-44,311,350,374,432`؛ `LoginAttemptRepository.cs:96`) | دالة T-SQL | DML | `COALESCE` (D-05) | 5 | 2 | 10 |
| INV-41 | 17 `DATEADD(` في 7 ملفات (`OwnershipTransferCodeRepository.cs:108`، `UserRepository.cs:822`، `NotificationOutboxRepository.cs:247`)؛ `DATEDIFF` `DashboardStatsRepository.cs:357` | حساب زمني | DML | `@Now - make_interval(secs => @WindowSeconds)`؛ `EXTRACT(EPOCH FROM ("EndedAt" - "StartedAt"))` (D-05) | 5 | 3 | 15 |
| INV-42 | `DashboardStatsRepository.cs:56,60,130,135,139,143,296,300` (`AT TIME ZONE 'UTC') AT TIME ZONE @TimeZone`)؛ `:656` (`TryConvertIanaIdToWindowsId`) | تحويل مناطق زمنية بأسماء Windows | DML/semantics | `("CreatedAt" AT TIME ZONE @TimeZone)::date` بأسماء IANA مباشرة؛ حذف التحويل إلى Windows (D-19) | 5 | 3 | 15 |
| INV-43 | `ApplicationRepository.cs:730`، `RoleRepository.cs:411`، `PermissionRepository.cs:675` | `STRING_AGG(…) WITHIN GROUP (ORDER BY …)` | DML | `string_agg(x."Name", ', ' ORDER BY x."Name")` (D-05) | 5 | 2 | 10 |
| INV-44 | `ApplicationRepository.cs:677,729`؛ `RoleRepository.cs:375`؛ `PermissionRepository.cs:620` | `CROSS APPLY (VALUES …)` / `OUTER APPLY` | DML | `CROSS JOIN LATERAL (VALUES …)` / `LEFT JOIN LATERAL … ON TRUE` (D-05) | 5 | 3 | 15 |
| INV-45 | `UserRepository.cs:595` (`CONVERT(NVARCHAR(200), @Id)` uppercase)؛ `RevokedTokenStore.cs:21-40` يكتب `revocation.Key` كما هو | مطابقة مفتاح GUID نصّي تعتمد على CI | DML/semantics | `@Id::text` + عمود `RevocationKey` من نوع `citext` (D-07) | 4 | 4 | 16 |
| INV-46 | 12 `N'…'` في 4 ملفات (`UserRepository.cs:675`، `ApplicationRepository.cs:320,329`، `DashboardStatsRepository.cs:176,311,314,350,353,374`، `Auth/Auth_Setup/Program.cs:41`) | national literals | DML | `'…'` (D-05) | 3 | 2 | 6 |
| INV-47 | `NotificationTemplateRepository.cs:315-317`؛ `NotificationOutboxRepository.cs:187-189` | `LIKE '%' + @SearchTerm + '%'` | DML | `ILIKE '%' \|\| @SearchTerm \|\| '%'` (D-07) | 5 | 3 | 15 |
| INV-48 | 31 `LIKE` في 9 ملفات (`grep -rn "\bLIKE\b" Auth/Auth.Infrastructure --include=*.cs \| wc -l`)؛ `Auth/Auth.Infrastructure/Persistence/UserSearchSql.cs:42-47`؛ `OrganizationRepository.cs:423,464,496` (`LIKE 'org:%'`)؛ `AuditLogRepository.cs:177`؛ لا `ESCAPE` (`grep -rn "ESCAPE" … \| wc -l` → 0) | بحث يعتمد على collation CI؛ `[` wildcard في SQL Server | semantics | `ILIKE`؛ `[` يصبح حرفًا عاديًّا (تضييق مقبول) (D-07) | 5 | 4 | 20 |
| INV-49 | 52 مساواة على أعمدة نصية (`grep -rnE "\[(Code\|Slug\|Username\|Name\|ClientId\|Key\|Handle\|Email\|SectionKey\|RevocationKey\|Token\|TokenHash\|KeyHash)\] = @" Auth/Auth.Infrastructure/Persistence \| wc -l`)؛ غير مُطبَّع: `LoginAttemptRepository.cs:119,158` (`[Username] = @Email` بقيمة خام `:121`)، `NotificationTypeRepository.cs:58`؛ حالات مختلطة: `ApplicationRepository.cs:82,151` و`RoleRepository.cs:42,56,183` upper مقابل `OrganizationRepository.cs:53,66` و`PermissionRepository.cs:42,245` lower | مساواة تعتمد على collation CI | semantics | أعمدة المفاتيح → `citext`؛ أعمدة hash/token → مقارنة دقيقة (D-07) | 5 | 5 | 25 |
| INV-50 | `OrganizationRepository.cs:1408,1427,1513-1514`؛ `Auth/Auth_DB/dbo/Tables/Organizations/OrganizationInvitations.sql:25,50` | `[Status] = 'Pending'` نصّي | semantics | `Status` → `citext` (القيم يولّدها التطبيق بثبات) (D-07) | 3 | 3 | 9 |
| INV-51 | 13 `QueryMultiple` في 7 ملفات (`DashboardStatsRepository.cs:30,118,283,338,405,492`، `UserRepository.cs:771`، …)؛ `MultipleActiveResultSets=true` في `Auth/Auth_API/appsettings.Development.json:19`؛ `grep -rn "WhenAll" Auth/Auth.Infrastructure/Persistence \| wc -l` → 0 | دفعات متعدّدة النتائج | DML | مدعومة في Npgsql بعبارات مفصولة بـ`;`؛ MARS غير مطلوب (لا تزامن على اتصال واحد) (D-05) | 3 | 3 | 9 |
| INV-52 | `Auth/Auth_API/Program.cs:725-728`؛ `Auth/Auth_API/Common/UtcDateTimeConverter.cs:29-31`؛ 203 `DateTime.UtcNow` في 85 ملفًا (`grep -rn "DateTime.UtcNow" Auth --include=*.cs \| grep -v Tests \| wc -l`)؛ لا TypeHandler | القيم تُقرأ `Unspecified` وتُكتب `Utc` | type/semantics | `SqlMapper.AddTypeHandler` لـ`DateTime` و`DateTime?` يفرض `Kind=Utc` في الاتجاهين (D-06) | 5 | 5 | 25 |
| INV-53 | `NotificationLayoutRepository.cs:130-131`؛ `NotificationTemplateRepository.cs:220-222` | تزامن متفائل بمساواة `[ModifiedAt] = @ExpectedRevisionAt` على DATETIME2(7) | semantics | يعمل على دقّة 6 إذا مرّت القيمة عبر القاعدة؛ اختبار round-trip (D-06) | 4 | 4 | 16 |
| INV-54 | 21 استدعاء `SortSql.OrderBy(…, "[Id]")` (`UserRepository.cs:742`، `AuditLogRepository.cs:219,243`، …) + `ORDER BY …, [Id]` ثابتة (`NotificationLayoutRepository.cs:34`، `NotificationTypeRepository.cs:31`، `UserKnownDeviceRepository.cs:74`، `UserSessionRepository.cs:282`)؛ `Auth/Auth.Infrastructure/Persistence/SortSql.cs:41-46` | tie-breaker على GUID عشوائي (`Guid.NewGuid()` 18 موضعًا، لا `CreateVersion7`) | semantics | ترتيب `uuid` بايتي يختلف عن `uniqueidentifier`؛ يُقبل كانحراف (D-17) | 3 | 2 | 6 |
| INV-55 | `Auth/Auth_API/appsettings.json:13-14`؛ `Auth/Auth_API/appsettings.Development.json:19`؛ `Auth/Auth.Application/Features/Secrets/SetConnectionString/SetConnectionStringCommandHandler.cs:123`؛ `Auth/Auth.Shared/Configuration/DpapiSecretConfigurationProvider.cs:183` | صيغة سلسلة اتصال SqlClient (`Data Source`, `Initial Catalog`, `Trusted_Connection`, `TrustServerCertificate`, `MultipleActiveResultSets`) | ops/config | `Host=…;Database=…;Username=…;Password=…;SSL Mode=Require;Timezone=UTC` (D-21) | 5 | 4 | 20 |
| INV-56 | `Auth/Auth_Setup/Program.cs:41` | يطبع `UPDATE [dbo].[Users] SET … N'…' WHERE [Email] = '…'` | ops | صيغة PostgreSQL مقتبسة | 3 | 2 | 6 |
| INV-57 | `Auth/Auth.Infrastructure/Security/EncryptionMigrationService.cs:61,74,81,93` | Dapper خارج Persistence بـquoting T-SQL و`NOT LIKE 'v2:%'` | DML | quoting فقط؛ `NOT LIKE` يبقى (البادئة ثابتة الحالة) (D-04) | 4 | 3 | 12 |
| INV-58 | `Auth/Auth_API.Tests/Infrastructure/*` (12 ملفًا تُثبت نصّ T-SQL؛ 9 تقرأ SSDT: `Auth/Auth_API.Tests/Infrastructure/PostDeploymentScriptTests.cs:243`، `Auth/Auth_API.Tests/Infrastructure/PlatformSeedContractTests.cs:124`، `Auth/Auth_API.Tests/Infrastructure/UserHardDeleteSqlTests.cs:198,259`، `Auth/Auth_API.Tests/Configuration/InvitationTokenHashingGuardTests.cs:60-81`، `Auth/Auth_API.Tests/Infrastructure/UserReadProjectionTests.cs:23-28`)؛ `Auth/Auth_API.Tests/Helpers/SqlExceptions.cs`؛ 7 ملفات تستورد SqlClient (`grep -rln "SqlException\|SqlClient" Auth/Auth_API.Tests --include=*.cs \| wc -l`)؛ `Auth/Auth_API.Tests/Helpers/RecordingDbConnectionFactory.cs:9-26`؛ 38 نسخة `SolutionDirectory()` (`grep -rn "string SolutionDirectory()" Auth/Auth_API.Tests \| wc -l`) | اختبارات مبنية على نصّ T-SQL وملفات SSDT وSqlException | test | تُنقل لتُثبت نصّ PostgreSQL وملفات DbUp و`PostgresException`؛ تُضاف طبقة اختبار ضد PostgreSQL حقيقي (D-22) | 5 | 5 | 25 |
| INV-59 | `.github/workflows/ci.yml:45,69` | `windows-latest` بلا قاعدة بيانات | test/ops | PostgreSQL على runner + متغير بيئة للاتصال (D-22) | 5 | 4 | 20 |
| INV-60 | `ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md:16-17,222,300-306,313-360,1436-1515`؛ `ReadMe/DEVELOPER_GUIDE.md:191,247,317-351`؛ `ReadMe/DEVELOPER_GUIDE.ar.md:112,149`؛ `ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md:63,871,894,1134,1154,1239,1334-1341`؛ `README.md:52,72,102,123,143,216`؛ 156 إصابة كلمات مفتاحية عبر الوثائق (`grep -rn -i "sqlpackage\|dacpac\|SSDT\|SQL Server\|…" README.md ReadMe docs Plans Auth/Auth_DB \| wc -l`)؛ انحراف قائم: «52 tables» مقابل 54، «SQL Server 2019» مقابل `Sql160` | وثائق النشر والتطوير والنسخ الاحتياطي | docs | إعادة كتابة الأقسام المعنية | 3 | 3 | 9 |
| INV-61 | `Auth_UI/packages/i18n/src/locales/en.ts:2212,2331` + 6 لغات (`grep -rln -i "sql server" Auth_UI/packages/i18n/src/locales`) | نصوص واجهة تذكر SQL Server | docs | إعادة صياغة النصوص السبع؛ `locales.test.ts` يحرس التكافؤ | 2 | 2 | 4 |
| INV-62 | `docs/api/error-codes.json:794` (`Http.Unavailable`)، `:1769` (`Persistence.ReferenceConflict`)؛ `Auth/Auth.Domain/Errors/PersistenceErrors.cs:10-11` | الأكواد التي تصلها أخطاء القاعدة | error | **لا تغيير** — E2 (D-12) | 1 | 5 | 5 |
| INV-63 | `grep -rn "SELECT \*" Auth/Auth.Infrastructure/Persistence \| wc -l` → 27 (`RefreshTokenRepository.cs:25,38,221`) | `SELECT *` يعتمد على أسماء الأعمدة كما في DDL | DML | يعمل مع quoted PascalCase (D-04) | 2 | 3 | 6 |
| INV-64 | 6 `IN @list` في 4 ملفات (`ApiKeyRepository.cs:238`، `OrganizationRepository.cs:306,328`، `NotificationTemplateRepository.cs:196,528`، `UserRepository.cs:130`) | توسيع قوائم Dapper | DML | Dapper يوسّعها إلى `(@p1,@p2,…)` مع Npgsql؛ اختبار تكامل يثبت ذلك (D-22) | 2 | 3 | 6 |
| INV-65 | 13 `BeginTransaction()` في 7 ملفات بلا isolation (`UserRepository.cs:296,526`، `PendingRegistrationRepository.cs:55,184`، …)؛ 52 تمرير `transaction` إلى Dapper | READ COMMITTED بالقفل في SQL Server (لا RCSI) | concurrency | READ COMMITTED بـMVCC؛ القرّاء لا يُحجَبون (توسيع مقبول) (D-10) | 2 | 3 | 6 |
| INV-66 | `Auth/Auth.Infrastructure/Persistence/SqlConnectionFactory.cs:13-17`؛ `Auth/Auth_API/Common/ConnectionStringGuard.cs`؛ `Auth/Auth_API.Tests/SecretManagement/ConnectionStringGuardTests.cs:55` | حارس placeholder لسلسلة الاتصال (مستقل عن المزوّد) | ops | يبقى؛ الاختبار يُحدَّث لصيغة Npgsql (D-21) | 1 | 2 | 2 |
| INV-67 | `ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md:1334-1341` (full + differential كل 6 ساعات، 30 يومًا)؛ `:1239` («recovery … is a database restore») | تشغيل النسخ الاحتياطي | ops | `pg_basebackup` + WAL archiving يوميًّا/مستمرًّا، أو `pg_dump -Fc` يوميًّا (D-21) | 3 | 4 | 12 |
| INV-68 | `Auth/Auth.Infrastructure/Persistence/RoleRepository.cs:264` (`CommandDefinition` بـtoken) + 3 مواضع أخرى (`grep -rn "CommandDefinition" Auth/Auth.Infrastructure/Persistence \| wc -l` → 4) | مهلة/إلغاء الأوامر | ops | نفسها؛ `CommandTimeout` الافتراضي في Npgsql 30 ثانية كما في SqlClient | 1 | 2 | 2 |

## 5. Semantic Traps

| T | الحكم | INV | الحلّ | D |
|---|---|---|---|---|
| T1 Collation | **ينطبق، الأعلى خطرًا.** `ModelCollation 1033, CI` بلا أي `COLLATE` صريح (`grep -rni COLLATE Auth/Auth_DB \| wc -l` → 0)؛ 52 مساواة و31 `LIKE` وقيود UNIQUE على `Username`/`Code` كلّها CI اليوم. PostgreSQL: مقارنة دقيقة افتراضيًّا؛ الـcollation غير الحتمي لا يدعم `LIKE` | INV-06, 45, 47, 48, 49, 50 | أعمدة المفاتيح النصية → `citext` (مساواة وUNIQUE وLIKE كلّها CI، accent-sensitive كما `_CI_AS`)؛ كل بحث `LIKE` → `ILIKE`؛ أعمدة hash/token/base64 تبقى `varchar` بمقارنة دقيقة (تشديد أمني مقبول)؛ collation القاعدة ICU `und` لترتيب ORDER BY قريب من Windows 1033؛ اختبار حارس يمنع أي `= @` على عمود نصّي غير citext غير مُوثَّق | D-07 |
| T2 Identifier folding | **ينطبق.** كل الأسماء PascalCase ومقتبسة بالأقواس (933 في DDL، 635 في C#)؛ أعمدة تُطابق كلمات PG غير محجوزة (`Key`, `Value`, `Timestamp`, `Action`, `Level`, `Version`, `Name`) | INV-02, 29, 57, 63 | إبقاء PascalCase مع اقتباس مزدوج في **كل** موضع (DDL وDML)؛ إسقاط `dbo.` والاعتماد على `public`؛ إعادة كتابة آلية `\[(\w+)\]` → `"$1"` مع مراجعة الفروق | D-04 |
| T3 GUID ordering | **ينطبق على tie-breakers فقط.** 21 استعلامًا مرقّمًا يكسر التعادل بـ`[Id]` عشوائي؛ لا keyset pagination (`grep -rniE "keyset\|cursor\|LastId" … ` → 0) | INV-54 | ترتيب الصفوف **داخل التعادل** يتغيّر بين المحرّكين لكنه يبقى حتميًّا ومستقرًّا داخل PG؛ عقد الـAPI لا يعرّف ترتيب التعادل. يُقبل كانحراف موثّق؛ الاختبار يثبت الاستقرار لا المطابقة | D-17 |
| T4 Time | **ينطبق.** 144 `DATETIME2(7)` تُقرأ `Unspecified`؛ Npgsql يرفض كتابة `Unspecified` إلى `timestamptz` ويرفض `Utc` إلى `timestamp`؛ 150 `GETUTCDATE()` (زمن العبارة) مقابل `now()` (زمن المعاملة)؛ `AT TIME ZONE` بأسماء Windows | INV-04, 30, 41, 42, 52, 53 | `timestamptz` + `TimeZone=UTC` على الاتصال + Dapper `TypeHandler<DateTime>` يفرض `Kind=Utc` قراءةً وكتابةً؛ `now()` مكان `GETUTCDATE()` (انحراف: تساوي الطوابع داخل المعاملة الواحدة)؛ دقّة 100ns → 1µs (Npgsql يقصّ عند الكتابة؛ checksum يُطبَّع إلى µs)؛ أسماء IANA مباشرة وحذف تحويل Windows | D-06, D-19 |
| T5 Concurrency | **ينطبق.** لا RCSI (0 إصابة) → SQL Server يحجب القرّاء بالأقفال؛ PG MVCC. 5 `UPDLOCK,HOLDLOCK` تعتمد على key-range lock لصفّ **غير موجود** (تعليق `PendingRegistrationRepository.cs:61-64`)؛ `FOR UPDATE` لا يقفل الغياب؛ 4 `MERGE`؛ SERIALIZABLE batch؛ `READPAST` | INV-33, 34, 35, 37, 65 | `pg_advisory_xact_lock(hashtextextended(key,0))` قبل `SELECT … FOR UPDATE` (يُسلسل البدايات لعنوان واحد كما يفعل key-range lock)؛ `MERGE HOLDLOCK` → `INSERT … ON CONFLICT DO UPDATE`؛ SERIALIZABLE batch → `ON CONFLICT ON CONSTRAINT` (بلا SERIALIZABLE فلا 40001 مصدره)؛ `READPAST` → `FOR UPDATE SKIP LOCKED`؛ إبقاء `catch 23505` كحارس ثانٍ؛ إعادة المحاولة عند `40P01` و`40001` | D-10 |
| T6 Optimistic concurrency | **ينطبق على جدول واحد.** `SystemSettingsOverrides.RowVersion` ROWVERSION يُقارن ويُسلسَل base64 (`Auth/Auth.Application/Features/SystemSettings/UpdateSystemSettings/UpdateSystemSettingsCommandHandler.cs:112`) | INV-08, 36 | `bigint` يزداد في كل UPDATE ويُقارن في WHERE؛ يُسلسَل 8 بايت big-endian → base64 فتبقى `RowVersion` في الجسم سلسلة base64 بطول 12 كما اليوم (E2). النسخ: `rowversion` 8 بايت → `bigint` بنفس البايتات | D-11 |
| T7 Error mapping | **ينطبق.** مترجم بالنوع الدقيق (`exception.GetType()`) لـ`SqlException`؛ 9 مواضع في المستودعات تقرأ `.Number` | INV-26, 27, 28, 62 | `PostgresException`: `23503` → `Persistence.ReferenceConflict` (409)؛ `08000/08001/08003/08006/57P01/57P02/57P03/53300/3D000` → outage (`Http.Unavailable` 503 مع `Retry-After`)؛ `23505` → null (500) كما اليوم. `NpgsqlException` (غير Postgres): `InnerException is TimeoutException` أو `IsTransient` → outage. لا كود جديد في `error-codes.json` | D-12 |
| T8 Strings | **ينطبق جزئيًّا.** SQL Server يتجاهل الفراغات الزائدة في `=`؛ PG لا. `NVARCHAR(n)` تعدّ وحدات UTF-16، `varchar(n)` تعدّ أحرفًا | INV-06, 49 | فحص ما قبل النسخ: عدّ الصفوف ذات فراغ زائد في أعمدة UNIQUE النصية يجب أن يكون 0 (وإلا قرار مالك)؛ التطبيق يُطبّع البريد (`Auth/Auth.Domain/ValueObjects/Email.cs:37,53`) ويقصّ في موضع واحد (`UserRepository.cs:240`). `varchar(n)` أوسع أو مساوٍ دائمًا فلا رفض لبيانات مقبولة؛ الـvalidators تحدّ الطول في C# (`Auth/Auth.Application/Validators/Rules/SharedValidationRules.cs:24-58`) | D-05, D-07 |
| T9 Booleans | **ينطبق.** 30 عمود BIT؛ 199 مقارنة بـ0/1؛ seed تُدرج `1`/`0` حرفيًّا (`Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql:51`) وتفشل على boolean | INV-05, 18, 39 | `boolean`؛ إعادة كتابة المقارنات والإسقاطات والمعاملات وحرفيات الـseed؛ Dapper يربط `bool` ↔ `boolean` بلا handler | D-08 |
| T10 Binary | **لا ينطبق إلا على ROWVERSION.** 0 `VARBINARY`؛ كل الهاشات والمشفّرات نصوص (`Auth/Auth_DB/dbo/Tables/Authentication/RefreshTokens.sql:5`، `Auth/Auth_DB/dbo/Tables/Security/UserEncryptionKeys.sql:4`، `Auth/Auth_DB/dbo/Tables/Core/Users.sql:54-56`) بترميز base64/`v2:`؛ `byte[]` في ملف واحد (`SystemSettingsRepository.cs:52,124,138`) | INV-08, 06 | round-trip بايتي مضمون بنسخ النصوص كما هي (UTF-16 → UTF-8 بلا تطبيع) ويثبته checksum؛ ROWVERSION ضمن T6 | D-11, D-18 |
| T11 Schema tooling | **ينطبق.** SSDT/DACPAC بلا مكافئ؛ `Upgrades/` تعمل في كل نشر وبعضها يكتب محتوى (سلسلة تخطيطات البريد `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql:15-26`)؛ seed idempotent بـ`IF NOT EXISTS` | INV-01, 16, 17, 18 | DbUp (`dbup-postgresql`) داخل `Auth/Auth_DB/Auth_DB.csproj`: `V0001__baseline.sql` (المخطط بعد كل الترقيات) + `Seed/*.sql` بنمط RunAlways تعكس **الحالة النهائية** لسلسلة الـUpgrades؛ journal `SchemaVersions`؛ الترقيات القديمة تُتحقَّق كـno-op على المصدر ثم تُحذف | D-14, D-15, D-16 |
| T12 Ops surface | **ينطبق.** صيغة الاتصال، probe، health check مخصّص بـT-SQL، حزمة health غير مستعملة، DACPAC في runbook، النسخ الاحتياطي، مزوّد التهيئة من القاعدة | INV-19–25, 55, 56, 59, 60, 66, 67 | Npgsql pooling المدمج (`Maximum Pool Size` الافتراضي 100 كما SqlClient)؛ `Timeout=5` في probe؛ health check بـ`to_regclass`؛ runbook: `dotnet run --project Auth/Auth_DB` مكان `SqlPackage`؛ `pg_basebackup`+WAL؛ PG على Windows Server بجوار IIS أو Linux (قرار مالك Q-04) | D-20, D-21 |

## 6. Decision Records

| D | السؤال | الاختيار | المرفوض (سطر لكلّ) | الدليل | كلفة العكس |
|---|---|---|---|---|---|
| D-01 | كيف يُختار المسار؟ | Track A إذا وُجد أي صفّ كتبه عميل في الإنتاج؛ وإلا Track B | «حسب وجود خادم إنتاج» — خادم فارغ لا يحتاج حفظًا | §2 | لا شيء |
| D-02 | نسخة PostgreSQL | **17** (الحدّ الأدنى 16: `IS JSON`؛ 15: `NULLS NOT DISTINCT`) | 14/15 — تفتقد `IS JSON` فتحتاج CHECK بديلًا؛ 18 — حديثة، دعم Npgsql/أدوات على Windows [UNVERIFIED] | INV-12, INV-13 | منخفضة قبل الإنتاج؛ متوسطة بعده (pg_upgrade) |
| D-03 | المشغّل | `Npgsql` (PostgreSQL License) مكان `Microsoft.Data.SqlClient`؛ إبقاء Dapper 2.1.79 | `Devart dotConnect` — تجاري؛ EF Core — ORM ممنوع (C4) | INV-19, 20 | إعادة كتابة كاملة — مرتفعة |
| D-04 | المعرّفات | PascalCase مقتبس `"X"` في كل موضع؛ إسقاط `dbo` والاعتماد على `public` | snake_case + `MatchNamesWithUnderscores` — يعيد تسمية 54 جدولًا ومئات الأعمدة ويلمس كل projection؛ إبقاء schema `dbo` في PG — يحمل SQL-Server-ism بلا فائدة | INV-02, 29 | منخفضة (تحويل regex في الاتجاهين) |
| D-05 | خريطة الأنواع والدوال | `uniqueidentifier`→`uuid`؛ `datetime2`→`timestamptz`؛ `bit`→`boolean`؛ `nvarchar(n)`→`varchar(n)` (أو `citext` بـD-07)؛ `nvarchar(max)`→`text`؛ `char(64)`→`char(64)`؛ `tinyint`→`smallint`؛ `int`→`integer`؛ `bigint`→`bigint`؛ `NEWID()`→`gen_random_uuid()`؛ `CONVERT(NVARCHAR(100),NEWID())`→`upper(gen_random_uuid()::text)`؛ `TOP`→`LIMIT`؛ `OFFSET/FETCH`→`LIMIT/OFFSET`؛ `OUTPUT`→`RETURNING`؛ `ISNULL`→`COALESCE`؛ `DATEADD`→interval؛ `DATEDIFF(SECOND)`→`EXTRACT(EPOCH …)`؛ `STRING_AGG WITHIN GROUP`→`string_agg(… ORDER BY)`؛ `APPLY`→`LATERAL`؛ `N''`→`''`؛ `ISJSON(x)=1`→`x IS JSON` | `timestamp` بلا منطقة — يمنع Npgsql كتابة `Kind=Utc` إليه ويصطدم بـ203 `DateTime.UtcNow`؛ `jsonb` للأعمدة JSON — يغيّر تمثيل النص (إعادة ترتيب المفاتيح) ويكسر checksum وE3 | INV-03–12, 31, 32, 38, 40–44, 46 | منخفضة لكل بند |
| D-06 | الطوابع الزمنية و`Kind` | `timestamptz` + `Timezone=UTC` في سلسلة الاتصال + `SqlMapper.RemoveTypeMap(typeof(DateTime))` ثم `AddTypeHandler` يقرأ ويكتب `Kind=Utc` (يحوّل `Local`→UTC ويختم `Unspecified` بـUTC)؛ دقّة 6 مقبولة | `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` — يُعيد `Kind=Local` عند القراءة ويُخفي الخطأ لا يصلحه؛ `DateTimeOffset` في الكيانات — يلمس Domain (122 موضعًا) | INV-04, 52, 53 | منخفضة (handler واحد) |
| D-07 | المساواة غير الحسّاسة للحالة | امتداد `citext` على أعمدة **المفاتيح المعرِّفة** (القائمة تُشتقّ من INV-49/50 في S-04: `Users.Username/Email`، `Roles.Code`، `Permissions.Code`، `Applications.Code`، `Organizations.Code`، `NotificationTypes.Code`، `ExternalAuthProviders.Code`، `LoginAttempts.Username`، `OrganizationInvitations.Email/Status`، `AccountDeletionVerifications.Email`، `EmailVerificationTokens.Email`، `UserUiPreferences.Key`، `RevokedTokens.RevocationKey`)؛ كل `LIKE` بحثي → `ILIKE`؛ أعمدة hash/token/stamp تبقى دقيقة؛ collation القاعدة `LOCALE_PROVIDER icu, ICU_LOCALE 'und'` | collation ICU غير حتمي على مستوى العمود — لا يدعم `LIKE` (خطأ «nondeterministic collations are not supported for LIKE») حتى PG 17 [UNVERIFIED لـ18]؛ تطبيع في التطبيق + فهارس `lower()` — يلمس 52 موضعًا ويحتاج أعمدة Normalized جديدة (يخالف C4)؛ `citext` على **كل** النصوص — كلفة مقارنة على أعمدة لا تحتاجها وتغيير ترتيب المساواة للهاشات | INV-06, 45–50 | متوسطة (ALTER TYPE لكل عمود، إعادة بناء فهارس) |
| D-08 | BIT | `boolean`؛ إعادة كتابة `= 1/0` و`CAST(… AS BIT)` و`THEN 1 ELSE 0` وحرفيات الـseed؛ الاختبارات تثبت غياب أي `= 1` على عمود `Is*` | `smallint` 0/1 — يكسر ربط Dapper `bool` في 8 استعلامات `ExecuteScalarAsync<bool>` وفي المعاملات | INV-05, 18, 39 | منخفضة |
| D-09 | UNIQUE مع NULL | `UNIQUE NULLS NOT DISTINCT` للقيود الخمسة | partial unique index لكل حالة (`WHERE "ApplicationId" IS NULL` + آخر `IS NOT NULL`) — يمنع استعمال `ON CONFLICT ON CONSTRAINT` في INV-35 | INV-13, 35 | منخفضة |
| D-10 | القفل والتزامن | `pg_advisory_xact_lock(hashtextextended(key,0))` + `FOR UPDATE` للمواضع الخمسة؛ `ON CONFLICT DO UPDATE` للـ4 MERGE وللـSERIALIZABLE batch؛ `FOR UPDATE SKIP LOCKED` للـoutbox؛ إعادة المحاولة عند `40P01`/`40001`؛ READ COMMITTED الافتراضي بلا تغيير | إبقاء SERIALIZABLE — يُدخل `40001` في مسار ساخن ويحتاج retry أوسع؛ `LOCK TABLE` — يحجب الجدول كلّه؛ الاكتفاء بـ`catch 23505` بلا advisory lock — يغيّر النتيجة (rotation مقابل collision) في `PendingRegistrationRepository.cs:55-105` | INV-33–37, 65 | متوسطة (9 مواضع يدوية) |
| D-11 | بديل ROWVERSION | `RowVersion bigint NOT NULL DEFAULT 1`، `SET "RowVersion" = "RowVersion" + 1`، مقارنة في WHERE؛ التسلسل: 8 بايت big-endian → base64 (شكل الجسم ثابت) | `xmin` — يدور عند 2³² ولا يُسلسَل بنفس الشكل؛ trigger + `bytea` — منطق في القاعدة وأداة جديدة | INV-08, 36 | منخفضة |
| D-12 | ترجمة الأخطاء | مترجمان مسجّلان (`PostgresException`، `NpgsqlException`) بنفس `IExceptionProblemTranslator`؛ نفس الأكواد المنشورة؛ `23505` يبقى 500 خارج المستودعات التي تلتقطه | ترجمة `23505` إلى 409 عامًّا — يغيّر E2 لمسارات لا تتوقّعه اليوم؛ مترجم واحد يفحص `is PostgresException` — البحث بالنوع الدقيق (`Auth/Auth.Shared/Http/ErrorContract/ErrorContractExceptionHandler.cs:77`) يتجاوزه | INV-26–28, 62 | منخفضة |
| D-13 | Stored procedures | inline الأربعة المستدعاة كنصّ SQL في `UserRepository`/`RefreshTokenRepository` (`sp_RevokeAllUserTokens` → CTE بـ`RETURNING` + `count`)؛ حذف الخمسة الميتة | نقلها إلى PL/pgSQL — لغة ثانية ومنطق في القاعدة يخالف `/backend-development` («SQL never decides a business outcome») | INV-16 | منخفضة |
| D-14 | أداة المخطط | DbUp (`dbup-postgresql`, MIT) في `Auth/Auth_DB/Auth_DB.csproj` يحلّ محلّ `Auth_DB.sqlproj` بنفس المجلد؛ يُشغَّل بـ`dotnet run --project Auth/Auth_DB -- <connection>` من runbook وCI؛ journal `SchemaVersions`؛ يُبنى داخل `Auth.sln` (يُزيل استثناء `.github/workflows/ci.yml:14-19`) | grate/Evolve (MIT) — مكافئة بلا ميزة؛ Flyway/Liquibase — Java runtime على خادم Windows؛ EF migrations — ORM؛ تشغيل الترحيل عند بدء `Auth_API` — app pools متعدّدة تحت IIS تتسابق | INV-01, 17, 18, 59 | متوسطة |
| D-15 | مصير `Upgrades/` | لا تُنقل. baseline = المخطط **بعد** الترقيات؛ قبل النسخ (Track A) تُشغَّل الـ11 على المصدر مرّة أخيرة ويُثبت أن كلًّا منها no-op (`@@ROWCOUNT` 0 أو رسالة «nothing to reconcile»)؛ Track B لا يحتاجها | ترجمة الـ11 إلى PG — كود ميت منذ اليوم الأول | INV-17 | لا شيء |
| D-16 | الـseed | ترجمة `SeedData/*` والأجزاء المضمَّنة في PostDeployment إلى `Auth/Auth_DB/Seed/*.sql` بنمط `INSERT … SELECT … WHERE NOT EXISTS (<نفس المسند>)` تعمل RunAlways؛ محتوى تخطيطات البريد = الحالة النهائية لسلسلة الترقيات، ويُتحقَّق منه بمقارنة صفوف `NotificationLayouts` بعد نشر DACPAC على قاعدة SQL Server نظيفة (S-16) | `ON CONFLICT DO NOTHING` — يغيّر المسند (PK بدل المفتاح الطبيعي)؛ تصدير الـseed من قاعدة حيّة — يحمل بيانات إنتاج | INV-18 | منخفضة |
| D-17 | ترتيب التعادل على GUID | يُقبل اختلاف ترتيب الصفوف داخل التعادل؛ يُثبت الاستقرار داخل PG | فهرس تعبيري يحاكي ترتيب `uniqueidentifier` (بايتات 10-15 ثم …) — كلفة على 21 استعلامًا لخاصية لا يعرّفها العقد | INV-54 | منخفضة |
| D-18 | أداة نسخ البيانات (Track A) | مشروع مؤقّت `Tools/PgMigrate` (.NET console داخل المستودع، يُحذف بعد الـcutover): `SqlDataReader` → `NpgsqlBinaryImporter` (COPY)، خريطة أنواع صريحة، تعطيل FK/فهارس أثناء التحميل ثم إعادتها، checksum مُطبَّع على الجانبين بنفس الكود (§8.3) | `pgloader` — لا بناء Windows، خريطة أنواع ضمنية (`datetime2` precision، `rowversion`)؛ AWS/Azure DMS — لا ينطبق على IIS on-prem؛ SSIS/linked server — يدوي بلا checksum | §8.3 | لا شيء (أداة مؤقّتة) |
| D-19 | مصدر الوقت | `GETUTCDATE()` → `now()` (زمن بدء المعاملة) مع `TimeZone=UTC`؛ `AT TIME ZONE` بأسماء IANA | `statement_timestamp()` — أقرب حرفيًّا لكن يُدخل طوابع غير متساوية داخل المعاملة الواحدة بلا فائدة؛ `clock_timestamp()` — غير قابل للتكرار داخل العبارة | INV-30, 41, 42 | منخفضة (استبدال نصّي) |
| D-20 | Health check | إعادة كتابة `DatabaseReadinessHealthCheck` بـNpgsql و`to_regclass`/`information_schema`؛ حذف `AspNetCore.HealthChecks.SqlServer`؛ **لا** إضافة `AspNetCore.HealthChecks.NpgSql` (الفحص المخصّص هو المعتمد بتعليق `Auth/Auth_API/Program.cs:1073-1080`) | الحزمة الجاهزة — تفتح اتصالًا لكل طلب، وهو ما رُفض صراحةً | INV-24, 25 | منخفضة |
| D-21 | سلسلة الاتصال والتشغيل | صيغة Npgsql (`Host;Port;Database;Username;Password;SSL Mode=Require;Trust Server Certificate=<per env>;Timezone=UTC;Maximum Pool Size=100`)؛ `NpgsqlConnectionStringBuilder{Timeout=5}` في probe؛ السرّ `ConnectionStrings.AuthDb` يُستبدل عبر واجهة الإدارة نفسها (`SetConnectionStringCommand`)؛ نسخ احتياطي: `pg_basebackup` + `archive_command` (PITR) أو `pg_dump -Fc` يوميًّا | إبقاء الصيغة القديمة مع تحويل — لا يوجد محلّل مشترك | INV-21, 55, 66, 67 | منخفضة |
| D-22 | الاختبارات وCI | نقل الـ12 ملف regex لتُثبت نصّ PG؛ نقل الـ9 قارئات SSDT إلى ملفات DbUp؛ `Helpers/PostgresExceptions.cs` مكان `SqlExceptions.cs` (لـ`PostgresException` مُنشئ عامّ يقبل `sqlState`)؛ **إضافة** اختبارات تكامل ضد PostgreSQL حقيقي لكل مستودع (سلسلة اتصال من `AUTH_TEST_PG` — تفشل لا تُتخطّى عند غيابها)؛ CI: PostgreSQL على `windows-latest` عبر action تثبيت أصلي [UNVERIFIED] أو job `ubuntu-latest` بـ`services: postgres` | Testcontainers — يحتاج Docker Linux على runner Windows (غير متاح)؛ mocks للاتصال — لا تكشف أخطاء SQL وهي أصل المشكلة | INV-58, 59 | منخفضة |
| D-23 | استراتيجية الـcutover (Track A) | إيقاف كامل داخل نافذة (gateway → 503 مع `Retry-After`)، نسخ دفعة واحدة، تحقّق، تبديل | dual-write — يلمس 46 مستودعًا ويضاعف مسارات الخطأ؛ CDC/replication (Debezium) — وسيط رسائل لا يملكه النظام؛ نسخ تدريجي بالـtimestamps — جداول كثيرة بلا `ModifiedAt` | §8.1 | مرتفعة إن ثبت من التدريب أن المدة تتجاوز الميزانية → عندها يُعاد النظر في dual-write (Q-02) |

## 7. Common Core Work Breakdown

| S | يعتمد على | الملفات (متحقَّق) | الإجراء | القبول القابل للقياس | INV/D |
|---|---|---|---|---|---|
| S-01 | — | `Auth/Auth.Infrastructure/Auth.Infrastructure.csproj:15`؛ `Auth/Auth_API/Auth_API.csproj:19` | إضافة `Npgsql` (PostgreSQL License — مشغّل)؛ حذف `Microsoft.Data.SqlClient` و`AspNetCore.HealthChecks.SqlServer` | `grep -rn "SqlClient\|HealthChecks.SqlServer" Auth/*/*.csproj` → 0؛ البناء ينجح | INV-19, 25 / D-03, D-20 |
| S-02 | S-01 | `Persistence/SqlConnectionFactory.cs`؛ `Persistence/IDbConnectionFactory.cs`؛ `Auth/Auth_API/Program.cs:356` | `NpgsqlConnectionFactory` بنفس الواجهة؛ تسجيل Dapper `DateTime` handler مرّة واحدة عند البدء | `grep -rn "SqlConnection\b" Auth --include=*.cs \| grep -v Tests` → 0؛ اختبار يكتب `DateTime.UtcNow` ويقرأه بـ`Kind == Utc` وفرق ≤ 1µs | INV-20, 52, 53 / D-03, D-06 |
| S-03 | S-01 | `Persistence/SqlConnectionStringProbe.cs`؛ `Auth/Auth_API/Program.cs:360`؛ `Auth/Auth.Application/Features/Secrets/SetConnectionString/SetConnectionStringCommandHandler.cs:123`؛ `Auth_API/appsettings*.json:12-19`؛ `Auth/Auth_API.Tests/SecretManagement/ConnectionStringGuardTests.cs:55` | `NpgsqlConnectionStringProbe`؛ تحديث placeholders وأمثلة الاتصال والتعليقات | probe يرفض سلسلة SqlClient بـ`IsWellFormed=false` ويقبل صيغة Npgsql؛ اختبارات SecretManagement خضراء | INV-21, 55, 66 / D-21 |
| S-04 | — | `Auth/Auth_DB/dbo/Tables/*/*.sql` (54) | كتابة `Auth/Auth_DB/Migrations/V0001__baseline.sql`: ترجمة الـ54 جدولًا بخريطة D-05، `citext` لقائمة D-07، `NULLS NOT DISTINCT` للـ5، generated column، 74 partial index، 7 INCLUDE، 5 `IS JSON`، `upper(gen_random_uuid()::text)` | `psql -f` ينجح على PG 17 نظيف؛ فحص آلي: عدد الجداول 54، الفهارس 136، FK 75، UNIQUE 33 من `pg_catalog`؛ اختبار يثبت أن كل عمود `= @` نصّي في INV-49 من نوع `citext` أو مُوثَّق دقيقًا | INV-02–15 / D-02, D-04, D-05, D-07, D-08, D-09 |
| S-05 | S-04 | `Auth/Auth_DB/Auth_DB.sqlproj`؛ `Auth/Auth.sln`؛ `.github/workflows/ci.yml:14-19,56-66` | مشروع `Auth/Auth_DB/Auth_DB.csproj` (console + `dbup-postgresql` MIT) يُشغّل `Migrations/` مرّة و`Seed/` دائمًا؛ إزالة sqlproj وbackup؛ إدراجه في `Auth.sln`؛ CI يبني الحلّ كاملًا | `dotnet build Auth/Auth.sln` ينجح؛ `dotnet run --project Auth/Auth_DB` على قاعدة فارغة ثم مرّة ثانية = idempotent (journal صفّ واحد للـbaseline، seed بلا صفوف جديدة) | INV-01, 17, 59 / D-14, D-15 |
| S-06 | S-04 | `Auth/Auth_DB/dbo/Scripts/SeedData/*.sql` (16)؛ `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql:39-657` | ترجمة الـseed إلى `Auth/Auth_DB/Seed/NN_*.sql` بنمط `INSERT … WHERE NOT EXISTS`، boolean literals، uuid literals، `now()`؛ حذف `PRINT`/`GO`/`DECLARE` | تشغيلان متتاليان: الثاني يُدرج 0 صفوف؛ عدد الصفوف المزروعة لكل جدول = ما ينتجه DACPAC على SQL Server نظيف (يُقاس في S-16) | INV-18 / D-16 |
| S-07 | S-02 | 46 ملفًا تحت `Persistence/` + `EncryptionMigrationService.cs` + `IdentifierKeyRegenerationGuard.cs` + `DbSettingsConfigurationProvider.cs` | تحويل آلي (script في scratch، لا يُلتزم): `\[dbo\]\.\[(\w+)\]`→`"$1"`، `\[(\w+)\]`→`"$1"`، `GETUTCDATE()`→`now()`، `ISNULL(`→`COALESCE(`، `N'`→`'`، `OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY`→`LIMIT @PageSize OFFSET @Offset`، `SELECT TOP (@n)`/`TOP 1`→`LIMIT` | `grep -rnE "\[dbo\]|\[\w+\]|GETUTCDATE|ISNULL\(|FETCH NEXT|SELECT TOP" Auth/Auth.Infrastructure Auth/Auth_API --include=*.cs` → 0 (باستثناء التعليقات المُراجَعة) | INV-29, 30, 31, 32, 38, 40, 46, 57, 63 / D-04, D-05, D-19 |
| S-08 | S-07 | 14 ملفًا في INV-39 | إعادة كتابة مقارنات BIT: `"IsX" = 1`→`"IsX"`، `= 0`→`NOT "IsX"`، `CAST(CASE … THEN 1 ELSE 0 END AS BIT)`→`EXISTS(…)`/`CASE … THEN TRUE ELSE FALSE END`، `@IncludeDeleted = 1`→`@IncludeDeleted` | `grep -rnE "\"Is\w+\" = [01]\b|AS BIT|THEN 1 ELSE 0 END AS" …` → 0؛ الاختبارات في S-19 تمرّ | INV-39 / D-08 |
| S-09 | S-07 | `UserRepository.cs:304,535,567`؛ `PendingRegistrationRepository.cs:55-105,184-200`؛ `RoleRepository.cs:264-295`؛ `AccountDeletionTombstoneRepository.cs:28`؛ `PrivacyPolicyVersionRepository.cs:165`؛ `PlatformSettingsRepository.cs:41`؛ `NotificationOutboxRepository.cs:80-83`؛ `SystemSettingsRepository.cs:62-90`؛ `UserSessionRepository.cs:404` | إعادة كتابة يدوية للقفل والـupsert: advisory lock + `FOR UPDATE`؛ `ON CONFLICT`؛ `SKIP LOCKED`؛ `UPDATE … RETURNING` + `bigint RowVersion`؛ `UPDATE TOP`→`ctid IN (… LIMIT)` | اختبار تزامن (S-19): 20 بداية تسجيل متزامنة لعنوان واحد → صفّ واحد وrotation للباقي؛ claim متزامن للـoutbox → لا رسالة تُدّعى مرّتين؛ تحديث `SystemSettings` بـ`RowVersion` قديم → `SystemSettings.ConcurrencyConflict` | INV-08, 33, 34, 35, 36, 37, 38 / D-09, D-10, D-11 |
| S-10 | S-07 | `UserRepository.cs:69,166`؛ `RefreshTokenRepository.cs:51,99`؛ `Auth/Auth_DB/dbo/StoredProcedures/**` (9) | inline الأربعة؛ حذف المجلد | `grep -rn "EXEC \|sp_" Auth --include=*.cs \| grep -v Tests` → 0؛ `Auth/Auth_API.Tests/Infrastructure/UserReadProjectionTests.cs:23-28` مُعاد توجيهه إلى النصّ المضمَّن | INV-16 / D-13 |
| S-11 | S-07 | `DashboardStatsRepository.cs:32-44,56-60,120-143,285-300,340-357,407,494,656`؛ `SecretRotationImpactRepository.cs:31-39`؛ `OwnershipTransferCodeRepository.cs:108`؛ `EmailVerificationTokenRepository.cs:107`؛ `SecretOperationChallengeRepository.cs:192`؛ `NotificationOutboxRepository.cs:247`؛ `UserRepository.cs:822`؛ `ApplicationRepository.cs:677,729,730`؛ `RoleRepository.cs:375,411`؛ `PermissionRepository.cs:620,675` | `DATEADD`/`DATEDIFF`→interval/EPOCH؛ `AT TIME ZONE` بأسماء IANA وحذف `TryConvertIanaIdToWindowsId`؛ `STRING_AGG`→`string_agg(… ORDER BY)`؛ `APPLY`→`LATERAL`؛ `DECLARE @x DATETIME2` → معاملات C# | `grep -rnE "DATEADD|DATEDIFF|WITHIN GROUP|APPLY|DECLARE @" …` → 0؛ اختبار `GetAuditStats` بمنطقة `Asia/Riyadh` يعطي نفس التجميع اليومي كالمرجع المحسوب في C# | INV-41–44 / D-05, D-19 |
| S-12 | S-07 | 9 ملفات INV-48 + `UserRepository.cs:595` | `LIKE`→`ILIKE` في البحث؛ `'%' + @x + '%'`→`'%' \|\| @x \|\| '%'`؛ `CONVERT(NVARCHAR(200), @Id)`→`@Id::text` | `grep -rn "\bLIKE\b" Auth/Auth.Infrastructure --include=*.cs \| grep -v "ILIKE\|NOT LIKE 'v2:%'"` → 0؛ اختبار بحث `"ADMIN"` يجد `admin@…` | INV-45, 47, 48, 50 / D-07 |
| S-13 | S-01 | `Auth/Auth_API/Common/Errors/SqlExceptionProblemTranslator.cs`؛ `Auth/Auth_API/Common/Errors/ApiErrorContractExtensions.cs:25`؛ 6 مواضع INV-27؛ 3 مواضع INV-28 | `PostgresExceptionProblemTranslator` + `NpgsqlExceptionProblemTranslator`؛ `when (ex.SqlState == "23505")`؛ `when (ex.SqlState is "40P01" or "40001")` | `grep -rn "SqlException\|\.Number is" Auth --include=*.cs \| grep -v Tests` → 0؛ اختبارات العقد: `23503`→409 `Persistence.ReferenceConflict`؛ `57P01`→503 مع `Retry-After`؛ `23505` بلا التقاط→500 `Http.Unexpected` | INV-26–28, 62 / D-12 |
| S-14 | S-01 | `Auth/Auth_API/Common/HealthChecks/DatabaseReadinessHealthCheck.cs:1,55-66,142-155`؛ `Auth/Auth_API.Tests/HealthChecks/DatabaseReadinessHealthCheckTests.cs:116-151` | `to_regclass`، `information_schema.columns` (`character_maximum_length >= 255`)؛ رسالة «Run Auth_DB migrations before this API» | الاختبارات السبعة خضراء؛ `/ready` = Healthy على قاعدة مُرحَّلة وDegraded على فارغة | INV-24 / D-20 |
| S-15 | S-01 | `Auth/Auth.Infrastructure/Configuration/DbSettingsConfigurationProvider.cs:151-155`؛ `Auth/Auth_API/Common/IdentifierKeyRegenerationGuard.cs:53-57`؛ `Auth/Auth_Setup/Program.cs:41` | Npgsql + quoting؛ نصّ `Auth_Setup` بصيغة PG | بدء التطبيق مع `AUTH_DISABLE_DB_SETTINGS` غير مضبوط يحمّل التجاوزات من PG (اختبار تكامل) | INV-22, 23, 56 / D-03 |
| S-16 | S-04, S-06 | `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql`؛ `Auth/Auth_DB/dbo/Tables/Notifications/NotificationLayouts.sql` | إثبات تكافؤ الـseed: نشر DACPAC الحالي على SQL Server نظيف (خطوة يدوية خارج المستودع) وتصدير الجداول المزروعة (Roles, Permissions, PermissionImplications, RolePermissions, ExternalAuthProviders, NotificationTypes, NotificationLayouts, NotificationTemplates*, PrivacyPolicy*, PlatformSettings, Users×2, UserRoles×2)، ثم تشغيل DbUp على PG نظيف ومقارنة الصفوف بعد التطبيع (§8.3) | تطابق 100% للأعمدة غير الزمنية وغير المولَّدة؛ الفروق الموثّقة فقط: `CreatedAt`، `Id` المولَّد عشوائيًّا حيث لا PK ثابت | INV-18 / D-16 |
| S-17 | S-05 | `.github/workflows/ci.yml` | إضافة PostgreSQL 17 إلى job الـbackend (تثبيت أصلي على Windows [UNVERIFIED] أو job Linux إضافي)؛ خطوة `dotnet run --project Auth/Auth_DB`؛ `AUTH_TEST_PG` كمتغير | CI أخضر مع تنفيذ اختبارات S-19 (لا skip) | INV-59 / D-22 |
| S-18 | S-07–S-13 | 12 ملفًا في INV-58؛ `Auth/Auth_API.Tests/Helpers/SqlExceptions.cs`؛ 9 قارئات SSDT؛ `Auth/Auth_API.Tests/Infrastructure/ApiSourceScan.cs:36-45` | نقل التوقّعات إلى نصّ PG وملفات `Auth/Auth_DB/Migrations|Seed`؛ `Helpers/PostgresExceptions.cs` بـ`new PostgresException(msg, severity, invariantSeverity, sqlState)`؛ توحيد `SolutionDirectory()` في `ApiSourceScan` (اختياري، DRY) | `dotnet test` → 2000/2000 خضراء؛ `grep -rn "SqlClient\|SqlException\|Auth_DB.sqlproj\|\[dbo\]" Auth/Auth_API.Tests` → 0 | INV-58 / D-22 |
| S-19 | S-04–S-15 | جديد: `Auth/Auth_API.Tests/Infrastructure/Postgres/*RepositoryPgTests.cs` (46 مستودعًا) + `PgFixture.cs` | اختبارات تكامل ضد PG حقيقي: كل دالة عامّة في كل مستودع تُنفَّذ مرّة على الأقل على قاعدة مُرحَّلة ومزروعة؛ سيناريوهات التزامن في S-09؛ استقرار ترتيب التعادل على `"Id"` (D-17)؛ إلغاء عبر `CommandDefinition` يصل إلى Npgsql؛ round-trip للأنواع (uuid, timestamptz µs, boolean, citext, smallint↔enum, `IN @list`, `QueryMultiple`) | تغطية أسطر `Auth/Auth.Infrastructure/Persistence/**` تحت هذا التشغيل ≥ 95% (coverlet)؛ 0 استثناء `Npgsql`/`Postgres` غير متوقّع | INV-51, 54, 64, 65, 68 / D-17, D-22 |
| S-20 | S-19 | `Auth/Auth_API.Tests/ErrorContract/*` (`WebApplicationFactory`) | تشغيل اختبارات العقد والـAPI الحالية ضد PG لإثبات E2 | 0 فرق في status/`code`/body مقارنةً بالتشغيل الحالي (snapshot الاختبارات نفسها) | INV-62 / D-12 |
| S-21 | S-05 | `README.md:52,72,102,123,143,216`؛ `ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md:16-17,222,300-360,1436-1515`؛ `ReadMe/DEVELOPER_GUIDE.md:191,247,317-351`؛ `ReadMe/DEVELOPER_GUIDE.ar.md:112,149`؛ `ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md:63,871,894,1134,1154,1239,1334-1341` (والنسخة AR المقابلة)؛ `docs/adr/0002-postgresql.md` (من Appendix A) | إعادة كتابة: المتطلّبات، Phase 2 (`dotnet run --project Auth/Auth_DB`)، سلسلة الاتصال، النسخ الاحتياطي (`pg_basebackup`/WAL)، «54 tables»، إزالة كل SQL Server/SSDT/DACPAC | `grep -rn -i "dacpac\|sqlpackage\|SSDT\|SQL Server" README.md ReadMe docs` → 0 خارج ADR 0002 وقسم «التاريخ» | INV-60, 67 / D-14, D-21 |
| S-22 | — | `Auth_UI/packages/i18n/src/locales/{en,ar,fa,fr,tr,ur,zh}.ts` (السطران المقابلان لـ`en.ts:2212,2331`) | إعادة صياغة النصّين في 7 لغات | `locales.test.ts` أخضر؛ `grep -rn -i "sql server" Auth_UI/packages/i18n` → 0 | INV-61 |
| S-23 | S-19 | جديد: `Tools/perf/k6-hot-paths.js` + `Tools/perf/README.md` | تعريف الاستعلامات العشر الأسخن (§10 E5) وسيناريو حمل k6 (AGPL-3.0، أداة تطوير لا تُوزَّع) يقيس p95 لكل مسار API؛ يُشغَّل على SQL Server (baseline) ثم على PG | ملف نتائج لكل تشغيل؛ p95(PG) ≤ 1.2 × p95(SQL) لكل مسار | INV-31, 32, 48 / D-23 |
| S-24 | S-04 | `Auth/Auth_DB/Migrations/V0001__baseline.sql` | فهرس `pg_trgm` GIN اختياري لأعمدة بحث `UserSearchSql` **فقط إن** فشل S-23 على مسار بحث المستخدمين | يُضاف فقط بدليل قياس؛ يُوثَّق في ADR 0002 | INV-48 / D-07 |

## 8. Track A — Live Production

### 8.1 Strategy & downtime math

الاستراتيجية (D-23): إيقاف كامل، نسخ دفعة واحدة، تحقّق، تبديل. المدة:

```
T_window = T_freeze + T_noop + T_copy + T_verify + T_switch + T_smoke
T_freeze = إيقاف app pools + gateway 503                         ≈ 2 min (يُقاس في التدريب)
T_noop   = تشغيل الـ11 Upgrade على المصدر وإثبات 0 صفوف         ≈ 1–3 min (يُقاس)
T_copy   = Σ_t rows(t) / throughput_measured(t)                   (من التدريب الأول؛ COPY binary عادةً 50k–200k rows/s لكل جدول)
T_verify = 2 × Σ_t rows(t) / read_throughput + count(*) ×2 جانب  (checksum يقرأ كل صفّ مرّتين)
T_switch = تحديث سرّ ConnectionStrings.AuthDb + recycle + /ready  ≈ 3 min
T_smoke  = قائمة §8.5                                             ≈ 15 min
الميزانية: T_window × 1.3 ≤ max_downtime (Q-02)، وإلا: (أ) تقسيم النسخ إلى «مسبق» للجداول الثابتة بعد تجميدها منطقيًّا (Roles, Permissions, Applications, NotificationTemplates…) و«نهائي» للجداول الحيّة؛ (ب) فقط إن بقي التجاوز: dual-write — يُعاد فتح D-23.
```

### 8.2 Tasks

| A | يعتمد على | الملفات | الإجراء | القبول | Rollback | INV/D |
|---|---|---|---|---|---|---|
| A-01 | S-05 | جديد `Tools/PgMigrate/PgMigrate.csproj` (+ `Npgsql`, `Microsoft.Data.SqlClient` مؤقّتًا داخل الأداة فقط) | أداة النسخ: لكل جدول `SELECT` بترتيب PK → COPY binary؛ خريطة الأنواع D-05/D-07/D-11؛ ترتيب الجداول topologically حسب الـ75 FK؛ `SET session_replication_role = replica` أثناء التحميل ثم إعادة الفهارس | تدريب على قاعدة مزروعة صغيرة: E3 تمرّ؛ أداة تُبلّغ throughput لكل جدول | حذف الأداة (لا أثر على الإنتاج) | D-18 |
| A-02 | A-01 | `Tools/PgMigrate` (وضع `verify`) | حساب checksum §8.3 وcount لكل جدول على الجانبين وإخراج تقرير فرق | تقرير بـ0 فرق على قاعدة التدريب | — | D-18 |
| A-03 | A-01 | `Tools/PgMigrate` (وضع `preflight`) | فحوص ما قبل النسخ على المصدر: فراغات زائدة في أعمدة UNIQUE النصية (T8)؛ تكرارات ستصطدم بـ`citext` UNIQUE (`SELECT lower(Username), COUNT(*) … HAVING COUNT(*)>1`)؛ صفوف `NULL` مكرّرة في القيود الخمسة؛ قيم `TimeZone` غير IANA في `Users.TimeZone`؛ صفوف قديمة في `Username` بصيغة local-part (`Auth/Auth_DB/dbo/Tables/Core/Users.sql:4-10`) — تُعدّ فقط | كل عدّاد = 0 أو قرار مالك مسجَّل لكل استثناء | — | INV-06, 13, 42, 49 / D-07 |
| A-04 | S-05 | `Auth/Auth_DB` | تشغيل DbUp على PG الإنتاج الفارغ **بدون** Seed (وضع `--schema-only`) قبل النسخ؛ الـseed **لا يُشغَّل** في Track A لأن الصفوف المزروعة تأتي من المصدر؛ يُشغَّل بعد النسخ ويجب أن يُدرج 0 صفوف | `SchemaVersions` صفّ واحد؛ seed بعد النسخ = 0 إدراج | `DROP DATABASE` وإعادة الإنشاء | INV-18 / D-14, D-16 |
| A-05 | A-03 | المصدر (SQL Server) | تشغيل الـ11 Upgrade + `RECONCILE_prod_constraint_names.sql` مرّة أخيرة على المصدر داخل النافذة وإثبات no-op | لكل نصّ: صفوف متأثّرة 0 | لا تغيير حدث | INV-17 / D-15 |
| A-06 | S-23 | `Tools/perf` | قياس baseline p95 على SQL Server الإنتاج (أو staging بحجم الإنتاج) قبل النافذة | ملف baseline محفوظ | — | E5 |
| A-07 | A-01–A-05, S-01–S-20 | `ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md` (قسم runbook جديد) | كتابة runbook §8.5 وتنفيذ التدريبين §8.4 | تدريبان بتوقيتات مسجَّلة؛ T_window × 1.3 ≤ الميزانية | — | D-23 |
| A-08 | A-07 | إعدادات الخادم | تركيب PostgreSQL 17، `pg_hba.conf` بـ`scram-sha-256`، TLS، دور `auth_app` بأقلّ صلاحيات (بدون `CREATE` على schema بعد الترحيل)، `archive_command`/`pg_basebackup` مجدول، `pg_stat_statements` | `/ready` Healthy من بيئة staging؛ نسخة احتياطية أولى مُستعادة بنجاح على خادم آخر | إزالة الخدمة | INV-67 / D-02, D-21 |
| A-09 | A-08 | — | **الـcutover** وفق §8.5 | كل بوابات §8.5 خضراء | §8.6 | D-23 |
| A-10 | A-09 + 30 يومًا | `Tools/PgMigrate`، SQL Server | حذف الأداة من المستودع؛ إيقاف SQL Server بعد احتفاظ 30 يومًا بنسخة `.bak` نهائية | `git ls-files Tools/PgMigrate` → 0؛ `.bak` مؤرشف | استعادة `.bak` | D-18 |

### 8.3 Data migration & E3 validation method

**النسخ:** لكل جدول من الـ54 بترتيب FK: قراءة بترتيب PK، تحويل صريح، COPY binary. الأعمدة المُستثناة من النسخ: `Users.FullName` (generated؛ يُتحقَّق منها بالمساواة بعد النسخ)؛ `SystemSettingsOverrides.RowVersion` (تُحوَّل: 8 بايت big-endian → `bigint`).

**checksum(t)** يُحسب بنفس كود C# على الجانبين (لا دوال قاعدة): لكل صفّ بترتيب PK تُبنى سلسلة canonical ثم SHA-256، وchecksum الجدول = SHA-256 لسلسلة هاشات الصفوف. التطبيع لكل نوع:

| النوع | التطبيع |
|---|---|
| `uniqueidentifier`/`uuid` | `Guid.ToString("D")` lowercase |
| `datetime2`/`timestamptz` | قصّ إلى 1µs ثم `yyyy-MM-ddTHH:mm:ss.ffffff` UTC (`Kind` يُتجاهل) |
| `bit`/`boolean` | `0`/`1` |
| نصوص (`nvarchar`/`varchar`/`citext`/`text`/`char(64)`) | السلسلة كما هي بدون trim ولا تطبيع Unicode (UTF-16 → UTF-8 عند الحساب على الجانبين) |
| أعداد | تمثيل عشري invariant |
| `rowversion`/`bigint RowVersion` | القيمة العددية big-endian |
| NULL | الحرفي `\N` |

**E3 pass:** لكل جدول `rows_src = rows_dst` (من `COUNT(*)` مستقل عن الأداة على الجانبين) **و** `checksum_src = checksum_dst`. إضافةً: `Users.FullName` في PG = التطبيع نفسه لـSQL Server؛ عيّنة 100 صفّ عشوائي لكل جدول hash/encrypted (`RefreshTokens.TokenHash`, `Users.PasswordHash`, `Users.PhoneNumber`, `TwoFactorAuth.SecretKey`, `UserEncryptionKeys.WrappedDek`) تُقارن بايتًا بايتًا.

### 8.4 Rehearsals

| التدريب | البيئة | ما يُقاس | معيار النجاح |
|---|---|---|---|
| R1 | staging: استعادة `.bak` بحجم الإنتاج إلى SQL Server + PG 17 فارغ | throughput لكل جدول، T_copy، T_verify، أخطاء التحويل | E3 تمرّ؛ حساب T_window الأول |
| R2 | نفسها، بعد إصلاح ما ظهر في R1، بتنفيذ runbook §8.5 حرفيًّا بالتوقيت وبتنفيذ rollback §8.6 كاملًا | T_window كامل، T_rollback | E3 تمرّ؛ T_window × 1.3 ≤ الميزانية؛ rollback يعيد الخدمة على SQL Server خلال ≤ 10 min |
| R3 (إن اختلف R2 عن R1 بأكثر من 20%) | نفسها | تثبيت الرقم | فرق ≤ 20% |

### 8.5 Cutover runbook

| T-minus | الخطوة | البوابة |
|---|---|---|
| T-7d | A-08 مكتمل؛ R2 مسجَّل؛ إعلان للعملاء بنافذة الصيانة (§8.7) | موافقة المالك على T_window |
| T-1d | نسخة `.bak` كاملة؛ A-06 baseline محفوظ؛ نسخة من ملف `secrets.dpapi` وDataProtection key ring خارج الخادم | استعادة تجريبية للـ`.bak` |
| T-30m | تجميد النشر؛ فتح جلسة على الخادمين؛ تجهيز أوامر الأداة | — |
| T-0 | gateway → وضع صيانة (503 + `Retry-After`)؛ إيقاف app pool لـ`Auth_API` (يوقف `NotificationOutboxDispatcher` و`TokenRevocationBackgroundService` و`EncryptionMigrationService`) | لا اتصالات نشطة على SQL Server (`sys.dm_exec_sessions`) |
| T+2m | A-05 (Upgrades no-op) | 0 صفوف |
| T+5m | `PgMigrate preflight` (A-03) | كل العدّادات 0 |
| T+7m | `PgMigrate copy` | لا أخطاء |
| T+7m+T_copy | `PgMigrate verify` (E3) + seed DbUp = 0 إدراج (A-04) | **بوابة التحقّق النهائية** — كل الجداول متطابقة |
| بعد البوابة | تحديث سرّ `ConnectionStrings.AuthDb` (عبر `Auth_Setup`/واجهة الإدارة أو ملف secrets) إلى صيغة Npgsql؛ بدء app pool؛ `/ready` Healthy | `/ready` 200 بـ`database: Healthy` |
| +3m | Smoke: تسجيل دخول بحساب اختبار، refresh token، استعراض `/api/v1/users?search=…`، تحديث إعداد نظام مع `RowVersion`، إرسال إشعار اختبار (outbox claim)، قراءة audit log مرقّم | كل طلب 2xx بنفس الأجسام المتوقّعة |
| +15m | **نقطة اللاعودة:** فتح gateway لحركة العملاء | — |
| +1h / +24h | مراقبة `pg_stat_statements` p95، أخطاء 5xx، `/ready` | p95 ضمن E5 |

### 8.6 Rollback & point of no return

- **قبل نقطة اللاعودة** (أي خطوة حتى Smoke): إعادة السرّ إلى سلسلة SQL Server + بدء app pool + فتح gateway. SQL Server لم يُمسّ (A-05 no-op فقط). زمن الرجوع ≤ 10 min (يُقاس في R2).
- **نقطة اللاعودة = أول كتابة عميل على PG بعد فتح gateway.** ما قبلها لا يوجد أي تغيير لا يُعكس.
- **بعد نقطة اللاعودة (حتى 24 ساعة):** إغلاق gateway، `PgMigrate reverse-delta` يعيد الصفوف التي `CreatedAt > T_cutover` من الجداول append-mostly (`AuditLogs`, `LoginAttempts`, `RefreshTokens`, `UserSessions`, `NotificationOutbox`, `PendingRegistrations`, `Users` الجدد) إلى SQL Server، ثم إعادة السرّ. التحديثات في الموضع (lockout counters, `LastLoginUtc`, تحديثات إعدادات) خلال تلك المدة **تُفقد** — قرار مالك مسجَّل (Q-06). بعد 24 ساعة: الرجوع = استعادة من نسخة PG لا من SQL Server.

### 8.7 Customer impact

| البند | الأثر | الدليل |
|---|---|---|
| Access tokens (JWT 15 دقيقة) | لا أثر: التحقّق بمفتاح التوقيع على الخادم لا بالقاعدة؛ أثناء النافذة كل الطلبات 503 | `Auth/Auth.Application/Configuration/JwtSettings.cs:23`؛ `SigningKeyHealthCheck` في `Auth/Auth_API/Program.cs:1086` |
| Refresh tokens / Sessions | تبقى صالحة: صفوف `RefreshTokens`/`UserSessions` تُنسخ والهاش HMAC بمفتاح على الخادم (`Jwt:RefreshTokenHmacKeyPlain`) | `Auth/Auth_DB/dbo/Tables/Authentication/RefreshTokens.sql:5`؛ `Auth/Auth_DB/dbo/Scripts/Upgrades/2026-08-30_InvitationTokenHashing.sql:18-21` |
| البيانات المشفّرة (`PhoneNumber`, `TwoFactorAuth.SecretKey`, `WrappedDek`) | تبقى قابلة للفكّ: نصوص `v2:base64` تُنسخ كما هي؛ مفاتيح DataProtection على الخادم | `Auth/Auth_DB/dbo/Tables/Core/Users.sql:54-56`؛ `Auth/Auth.Infrastructure/Security/PerUserCryptoService.cs:22,78,146,170` |
| كلمات المرور | Argon2 نصّي، بلا تغيير | `Auth/Auth_DB/dbo/Tables/Core/Users.sql:14`؛ `Auth/Auth_Setup/Program.cs:33-36` |
| Revocation list في الذاكرة | تُعاد من `RevokedTokens` بعد البدء | `Auth/Auth.Infrastructure/Authentication/TokenRevocationBackgroundService.cs:64` |
| رسائل outbox غير المرسلة | تُنسخ بحالتها وتُستأنف بعد البدء (at-least-once) | `NotificationOutboxRepository.cs:80-83` |
| التواصل | إشعار قبل 7 أيام و24 ساعة بنافذة T_window × 1.3؛ أثناءها 503 بـ`Retry-After` = نهاية النافذة | §8.5 |

## 9. Track B — Pre-Production

| B | يعتمد على | الملفات | الإجراء | القبول | INV/D |
|---|---|---|---|---|---|
| B-01 | S-01–S-24 | — | تنفيذ Common Core كاملًا | §13 | INV-01..68 / D-01 |
| B-02 | S-05 | `Auth/Auth_DB/Auth_DB.sqlproj`، `Auth_DB.sqlproj_backup`، `dbo/StoredProcedures/**`، `dbo/Scripts/Upgrades/**`، `dbo/Scripts/*.sql`، `dbo/Tables/**`، `dbo/SeedData/**`، `dbo/PostDeployment/**` | حذف كل SSDT بعد اكتمال `Migrations/` و`Seed/`؛ يُنقل تاريخ الـUpgrades إلى ADR 0002 كسجلّ | `git ls-files Auth/Auth_DB` = `Auth_DB.csproj`, `Program.cs`, `Migrations/`, `Seed/` فقط | INV-01, 16, 17, 18 / D-14, D-15 |
| B-03 | S-03 | `Auth/Auth_API/appsettings.Development.json:19`؛ جديد `Auth/Auth_DB/docker-compose.postgres.yml` | سلسلة تطوير `Host=localhost;Database=Astoom_Auth;Username=auth;Password=<dev>;Timezone=UTC`؛ compose بـ`postgres:17` لمن يملك Docker؛ توثيق التثبيت الأصلي على Windows | مطوّر جديد: clone → compose up → `dotnet run --project Auth/Auth_DB` → `dotnet run --project Auth/Auth_API` → `/ready` Healthy | INV-55 / D-21 |
| B-04 | S-21 | `ReadMe/DEVELOPER_GUIDE.md:317-351`، `.ar.md` | إعادة كتابة قسم إعداد القاعدة للمطوّر (لا SSDT، لا `SQLEXPRESS01`) | `grep -n "SQLEXPRESS01\|SSDT" ReadMe/DEVELOPER_GUIDE*.md` → 0 | INV-60 |
| B-05 | S-17 | `.github/workflows/ci.yml:14-19` | حذف تعليق «SSDT لا يُبنى» وبناء `Auth.sln` كاملًا | CI أخضر ببناء الحلّ | INV-59 |
| B-06 | B-01–B-05 | — | إعادة ضبط أي قاعدة SQL Server تطويرية (حذف `Astoom_Auth` المحلية) — خارج المستودع؛ لا بيانات تُحفظ بحكم D-01 | لا خطوة في الوثائق تشير إلى SQL Server | D-01 |

## 10. Verification Matrix

| E | الطريقة | الأداة | عتبة النجاح | المهمة |
|---|---|---|---|---|
| E1 | تشغيل الـ2000 اختبار الحالية بعد نقلها + اختبارات PG الحقيقية؛ لا `Skip` | `dotnet test` مع `AUTH_TEST_PG`؛ coverlet | 100% نجاح؛ 0 skipped؛ تغطية Persistence ≥ 95% تحت التشغيل الحقيقي | S-18, S-19, S-17 |
| E2 | اختبارات العقد الحالية (`WebApplicationFactory`) ضد PG؛ مقارنة status/`code`/body؛ `error-codes.json` بلا تغيير | `Auth/Auth_API.Tests/ErrorContract/*`؛ `git diff docs/api/error-codes.json` | 0 فرق؛ diff فارغ | S-13, S-20 |
| E3 (A) | count + checksum مُطبَّع لكل جدول من الـ54 على الجانبين؛ عيّنات بايتية للأعمدة الحسّاسة | `Tools/PgMigrate verify` | 54/54 متطابق؛ 0 فرق في العيّنات | A-02, A-09 |
| E4 | اختبارات دلالية: CI equality/uniqueness/ILIKE (T1)؛ استقرار الترتيب (T3)؛ round-trip µs وKind (T4)؛ سيناريوهات التزامن (T5)؛ `RowVersion` (T6)؛ trailing spaces preflight (T8)؛ boolean (T9) | اختبارات S-19 + `PgMigrate preflight` | كل سيناريو أخضر؛ كل انحراف له D-xx (D-07, D-11, D-17, D-19) | S-04, S-09, S-12, S-19, A-03 |
| E5 | p95 لعشرة مسارات: (1) login (`sp_GetUserByEmail` المضمَّن)؛ (2) refresh (`RefreshTokenRepository.cs:38`)؛ (3) session by hash + update (`UserSessionRepository.cs:99,155`)؛ (4) effective permissions (`PermissionRepository.cs:126,182` عبر `Auth/Auth.Application/Features/Authentication/Common/TokenClaimsResolver.cs:33,63`)؛ (5) revocation reload (`RevokedTokenStore.cs:47`)؛ (6) login attempt insert + recent failures (`LoginAttemptRepository.cs:119`)؛ (7) audit insert؛ (8) outbox claim (`NotificationOutboxRepository.cs:80`)؛ (9) users search (`UserRepository.cs:742-769`)؛ (10) audit paged filter (`AuditLogRepository.cs:224`) | k6 سيناريو واحد على الجانبين؛ `pg_stat_statements` وQuery Store للتأكيد | p95(PG) ≤ 1.2 × p95(SQL) لكل مسار (المعامل قابل للتعديل Q-05) | S-23, A-06, S-24 |

## 11. Risk Register

| # | الخطر | L | I | L×I | التخفيف | إشارة مبكرة |
|---|---|---|---|---|---|---|
| R-01 | فقدان CI في مساواة/بحث/تفرّد نصّي بصمت (تسجيل دخول يفشل لبريد بحروف كبيرة، تكرار `Code`) | 4 | 5 | 20 | D-07 + اختبار حارس S-04 + preflight A-03 | اختبار S-19 لبحث `"ADMIN"` أحمر؛ preflight يجد تكرارات lower() |
| R-02 | صفوف مكرّرة عبر NULL في القيود الخمسة | 4 | 5 | 20 | D-09 + فحص `pg_constraint` في S-04 | `ON CONFLICT ON CONSTRAINT "UQ_UserRoles"` يفشل بـ«no unique constraint matching» |
| R-03 | `DateTime.Kind` يرمي عند أول كتابة قيمة مقروءة | 5 | 4 | 20 | D-06 handler + اختبار round-trip S-02 | استثناء «Cannot write DateTime with Kind=Unspecified» في S-19 |
| R-04 | key-range lock المفقود يسمح ببدايتي تسجيل لعنوان واحد | 3 | 4 | 12 | advisory lock D-10 + اختبار تزامن S-09 | صفّان `PendingRegistrations` لعنوان واحد في الاختبار |
| R-05 | مدة النسخ تتجاوز الميزانية | 3 | 4 | 12 | تدريبان §8.4؛ نسخ مسبق للجداول الثابتة؛ dual-write كملاذ | R1: T_window × 1.3 > الميزانية |
| R-06 | seed PG ≠ نتيجة DACPAC (سلسلة تخطيطات البريد) | 3 | 3 | 9 | S-16 مقارنة صفوف | فرق في `NotificationLayouts.PublishedContent` |
| R-07 | اختلاف دقّة الطوابع يكسر التزامن المتفائل بالمساواة (INV-53) | 2 | 3 | 6 | القيمة المتوقّعة تأتي من القاعدة نفسها (مقصوصة)؛ اختبار S-19 | `Notification.ConcurrencyConflict` غير متوقّع |
| R-08 | أداء بحث المستخدمين (`ILIKE` على 5 أعمدة) | 3 | 3 | 9 | S-23 قياس؛ S-24 `pg_trgm` مشروط | p95 مسار (9) > 1.2× |
| R-09 | CI على Windows بلا PostgreSQL متاح | 3 | 3 | 9 | job Linux بديل (D-22) | فشل خطوة التثبيت |
| R-10 | مزوّد التهيئة من القاعدة (`DbSettingsConfigurationProvider`) يفشل قبل DI فيسقط البدء | 2 | 5 | 10 | S-15 + اختبار تكامل بدء؛ `AUTH_DISABLE_DB_SETTINGS` كمخرج طوارئ (`Auth/Auth_API/Program.cs:367-371`) | `/health` up و`/ready` down بعد التبديل |
| R-11 | `RowVersion` bigint: نسخ قيمة `rowversion` تتجاوز `long` | 1 | 2 | 2 | preflight يفحص الحدّ؛ تحويل بـ`unchecked` موثّق | فشل COPY على `SystemSettingsOverrides` |
| R-12 | حذف Upgrades قبل التأكّد من no-op فتُفقد مصالحة لصفوف قديمة (`Username` local-part) | 2 | 3 | 6 | A-05 قبل النسخ؛ A-03 يعدّ الصفوف القديمة | عدّاد local-part > 0 |

## 12. Open Questions & [UNVERIFIED]

| # | البند | ما يحسمه |
|---|---|---|
| Q-01 | أي مسار؟ | جواب المالك: هل كتب عميل صفًّا في الإنتاج؟ (D-01) |
| Q-02 | حجم كل جدول وأقصى downtime | `sys.dm_db_partition_stats` على الإنتاج؛ رقم من المالك بالدقائق |
| Q-03 | قبول الانحرافات D-07 (تشديد مقارنة الهاشات)، D-11، D-15، D-17، D-19 | موافقة كتابية في ADR 0002 |
| Q-04 | استضافة PostgreSQL ونسخته الدقيقة | قرار مالك؛ `SELECT version()` بعد التركيب |
| Q-05 | معامل E5 | افتراضي 1.2 ما لم يُغيَّر |
| Q-06 | قبول فقدان التحديثات في الموضع عند rollback بعد نقطة اللاعودة | قرار مالك (§8.6) |
| U-01 [UNVERIFIED] | دعم `LIKE` مع collations غير حتمية في PG 18 | `SELECT 'a' COLLATE ci LIKE 'A'` على PG 18؛ لا يغيّر D-07 |
| U-02 [UNVERIFIED] | action تثبيت PostgreSQL أصليًّا على `windows-latest` ونسختها | تشغيل CI تجريبي في S-17 |
| U-03 [UNVERIFIED] | إصدار Npgsql المتوافق مع .NET 10 وDapper 2.1.79 | `dotnet add package Npgsql` ثم S-19 |
| U-04 [UNVERIFIED] | هل توجد صفوف بفراغ زائد أو تكرار CI في أعمدة UNIQUE النصية في الإنتاج | A-03 preflight |
| U-05 [UNVERIFIED] | هل يُدرج التطبيق `SecurityStamp`/`ConcurrencyStamp` صراحةً أم يعتمد على DEFAULT (`Auth/Auth_DB/dbo/Tables/Core/Users.sql:34-35`) | `grep -n "SecurityStamp" Auth/Auth.Infrastructure/Persistence/UserRepository.cs` ضمن S-07 |
| U-06 [UNVERIFIED] | throughput COPY الفعلي على عتاد الإنتاج | R1 |
| U-07 [UNVERIFIED] | هل تستعمل الاختبارات الحالية أي `[Fact(Skip=…)]` يجب إزالته | `grep -rn "Skip =" Auth/Auth_API.Tests` ضمن S-18 |

## 13. Definition of Done

- [ ] E1: `dotnet test` → 2000+ اختبار، 100% نجاح، 0 skipped، تغطية Persistence ≥ 95% تحت PG حقيقي (S-18, S-19).
- [ ] E2: اختبارات العقد بلا فرق؛ `docs/api/error-codes.json` بلا تغيير (S-13, S-20).
- [ ] E3 (Track A): 54/54 جدولًا متطابق count+checksum؛ تقرير `PgMigrate verify` مؤرشف (A-02, A-09).
- [ ] E4: اختبارات T1/T3/T4/T5/T6/T8/T9 خضراء؛ كل انحراف يحمل D-xx مقبولًا (Q-03).
- [ ] E5: p95 لعشرة مسارات ≤ 1.2× (أو المعامل المعتمد) مع ملفي baseline ونتيجة (S-23, A-06).
- [ ] S7 traceability: كل INV-01..68 مذكور في مهمّة؛ كل مهمّة تذكر INV أو D؛ لكل E مهمّة تحقّق (هذا الملف، §7–§10).
- [ ] Track A: كل A-xx له rollback؛ نقطة اللاعودة موثّقة (§8.6)؛ تدريبان مسجَّلان (§8.4).
- [ ] لا أثر SQL Server: `grep -rn -i "SqlClient\|SqlException\|\[dbo\]\|GETUTCDATE\|dacpac\|sqlpackage\|SSDT" Auth Auth_UI README.md ReadMe docs --include=*.cs --include=*.md --include=*.json --include=*.ts` → 0 خارج ADR 0002.
- [ ] CI أخضر ببناء `Auth.sln` كاملًا وتشغيل الاختبارات ضد PostgreSQL (S-17, B-05).
- [ ] ADR 0002 مقبول ومكتوب في `docs/adr/0002-postgresql.md` (S-21).

## Appendix A. ADR draft

```markdown
# ADR 0002: PostgreSQL replaces SQL Server as the Auth database

## Status
Proposed — 2026-09-27. Supersedes the SSDT/DACPAC deployment model in `ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md` Phase 2.

## Context
The schema (54 tables, `Auth/Auth_DB`) is an SSDT project that only Visual Studio's MSBuild can build (`README.md:123`, `.github/workflows/ci.yml:14-19`); the data layer is Dapper over `Microsoft.Data.SqlClient` with T-SQL in 46 repositories (635 bracket-quoted identifiers, 150 `GETUTCDATE()`, 4 `MERGE`, 5 `UPDLOCK, HOLDLOCK`, 1 `READPAST`, 9 stored procedures of which 4 are called); error translation keys on `SqlException.Number`; the test project has no database and asserts T-SQL text by regex. Case-insensitivity comes from the model collation `1033, CI` alone; five UNIQUE constraints rely on SQL Server treating NULLs as equal.

## Decision
1. Engine: PostgreSQL 17 (minimum 16). Driver: Npgsql. Dapper, MediatR, ErrorOr, repository interfaces and layer layout are unchanged.
2. Identifiers keep their PascalCase names, double-quoted, in schema `public`.
3. Types: `uuid`, `timestamptz` (UTC, µs), `boolean`, `varchar(n)`/`text`, `citext` on identifier columns (Username, Email, Code, …), `smallint` for `tinyint`, `bigint` counter for `rowversion`, generated column for `Users.FullName`, `UNIQUE NULLS NOT DISTINCT` where a NULL takes part in a key.
4. Semantics: search uses `ILIKE`; hash/token columns compare exactly (stricter than before, accepted); `now()` replaces `GETUTCDATE()`; tie-break order on random uuids differs from SQL Server and is not contractual.
5. Concurrency: advisory transaction locks + `FOR UPDATE` replace key-range locks; `INSERT … ON CONFLICT` replaces `MERGE … HOLDLOCK` and the SERIALIZABLE batch; `FOR UPDATE SKIP LOCKED` replaces `READPAST`; retries key on SQLSTATE `40P01`/`40001`; unique violations on `23505`; FK violations `23503` map to `Persistence.ReferenceConflict`; connection/timeout states map to `Http.Unavailable`. No new error code.
6. Schema tooling: DbUp (`dbup-postgresql`, MIT) in `Auth/Auth_DB/Auth_DB.csproj`; one baseline migration (the post-upgrade schema) plus always-run idempotent seed scripts; the dated `Upgrades/` scripts are retired after being proven no-ops on the source.
7. Tests: the regex tests are ported; repository integration tests run against a real PostgreSQL in CI; skipping is not allowed.
8. Live production migrates by a single offline copy (`Tools/PgMigrate`, temporary) validated by per-table row counts and normalized SHA-256 checksums, rehearsed twice on a production-sized copy, with rollback defined up to the first customer write on PostgreSQL.

## Consequences
### Positive
- The database project builds with `dotnet build` on any OS; CI builds the whole solution and tests against the real engine.
- Readers no longer block on writers (MVCC); unique-violation and deadlock handling is explicit by SQLSTATE.
### Negative
- Case-insensitive behaviour is now an explicit per-column choice (`citext`) instead of a database-wide default; adding a new key column requires choosing it.
- Timestamp precision drops from 100 ns to 1 µs.
- Hash comparisons become case-sensitive (previously case-insensitive under `CI` collation).
### Neutral
- Row order among equal sort keys changes (random uuid byte order) and remains stable.

## Alternatives Considered
- Stay on SQL Server with a non-SSDT migration tool: keeps vendor lock and the Windows-only build; rejected.
- snake_case rename with Dapper name matching: touches every table, column and projection for no functional gain; rejected.
- Non-deterministic ICU collation per column: no `LIKE` support; rejected.
- EF Core migrations: introduces an ORM the architecture forbids; rejected.
- Online replication / dual-write for cutover: adds a broker or doubles write paths for a single-database system; kept only as a fallback if the rehearsed window exceeds the downtime budget.

## References
- `Plans/SQLSERVER_TO_POSTGRESQL_MIGRATION_PLAN.ar.md` (this plan: inventory INV-01..68, decisions D-01..23)
- `docs/adr/0001-error-contract.md`
```
