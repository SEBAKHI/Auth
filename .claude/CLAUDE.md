# تعليمات هذا المستودع

> **نطاق هذا الملف:** ما يخصّ **هذا المستودع وحده** — مسارات، ارتباطات أدوات، حقائق مُتحقَّق منها هنا ولا تنطبق في غيره.
>
> أمّا ما هو صحيح في كل مشروع — بروتوكول الاستدلال والإخراج، والمبادئ المعمارية، والمهارات العامّة — فمكانه `~/.claude/CLAUDE.md` و`~/.claude/skills/`، ويُحمَّل تلقائيًّا مع هذا الملف. **لا تُعِد كتابته هنا:** نسختان من قاعدةٍ واحدة تنحرفان بأوّل تعديل يصيب إحداهما، بلا أن يفشل شيء.

## المهارات الإلزامية لهذا المستودع

| المهارة | متى |
|---------|-----|
| `dotnet-architecture` | قبل كتابة أو مراجعة أي كود C# — المبادئ العشرة شرطُ اكتمالٍ لا اقتراح |
| `domain-driven-design` | قبل أي تغيير في `Auth/Auth.Domain/` — واقرأ قسم **«نموذج النطاق»** أدناه: ارتباطاتُه في هذا المستودع |
| `event-driven-architecture` | قبل إضافة حدثٍ أو معالجٍ أو تغيير طريقة النشر — واقرأ قسم **«الأحداث»** أدناه: ارتباطاتُه في هذا المستودع، وفيها انحرافاتٌ قائمة عن قواعد المهارة |
| `frontend-playbook` | **قبل** أي كود تحت `Auth_UI/` — يقرّر الشكل والصحّة. واقرأ قسم **«الواجهة»** أدناه: الثوابت والملفات التي كانت المهارة ترويها كأنها عامّة هي حقائق هذا المستودع |
| `shadcn` | **بعده**، لأي عمل على المكوّنات — يورّد القطع. واقرأ **القسم التالي**: ارتباطاتُه في هذا المستودع تختلف عن الافتراضي |

**الترتيب مقصود:** `frontend-playbook` يقرّر الشكل، و`shadcn` يورّد القطع. مكوّنٌ اختيرَ بدقّة ثم وُضع في لوح تمرير يسحقه، أو رُبط بمفتاح ذاكرة غير موسوم، أو أُعيد ضبطه بـ`useEffect` — يبقى عيبًا، ولا يظهر أيٌّ من ذلك في فرقٍ على مستوى المكوّن.

المهارات كلّها **على مستوى المستخدم** (`~/.claude/skills/`)، غير موردة في هذا المستودع. إن كانت مفقودة على جهازك فاستنسخها هناك — لا شيء في هذا المستودع سيجلبها لك.

---

## بروتوكول واجهة المستخدم — shadcn/ui (ارتباطات هذا المستودع)

هذه المهارة إلزامية لأي عمل داخل `Auth_UI/`. الواجهة كلها مبنية على shadcn/ui. **المهارة نفسها على مستوى المستخدم** (مشتركة بين المشاريع)، أمّا **الارتباطات أدناه فحقائق هذا المستودع وحده**. لا تعتمد على الذاكرة أو على أنماط shadcn العامة — اقرأ الملف المعني قبل كتابة أي `.tsx`.

**Skill root:** `~/.claude/skills/shadcn/` (user-level, shared across projects) — entry point: `SKILL.md`
Invoked with the `Skill` tool as `shadcn` (the skill is `user-invocable: false`, so there is no `/shadcn` slash command). Invoking the skill does **not** replace reading the specific rule file for the area you are touching.

**متى تُقرأ (mandatory triggers):** adding/composing a component, fixing or debugging UI, form layout, icons, spacing, dark mode, RTL, chat surfaces, or reviewing any UI diff.

| Path | Read it before |
|------|----------------|
| `SKILL.md` | Any UI task — principles, critical rules, component-selection table |
| `rules/styling.md` | Writing `className`, spacing, sizing, dark mode, `cn()`, z-index |
| `rules/forms.md` | Any form: `FieldGroup`/`Field`, `InputGroup`, `ToggleGroup`, validation states |
| `rules/composition.md` | Groups, overlays, Card, Tabs, Avatar, Alert, Empty, Separator, Skeleton, Badge |
| `rules/base-vs-radix.md` | Custom triggers (`asChild` vs `render`), Select, Slider, Accordion APIs |
| `rules/icons.md` | Any icon usage — `data-icon`, no sizing classes |
| `rules/chat.md` | Conversation/messaging UI primitives |
| `cli.md` | CLI commands, flags, presets, templates (read the CLI override below first) |
| `customization.md` | Theming, CSS variables, extending a component |
| `registry.md` | Authoring or consuming a registry |
| `mcp.md` | Reference only — no shadcn MCP server is configured in this repo |

**Project bindings — الحقائق المثبتة لهذا المستودع** (source: [`Auth_UI/components.json`](../Auth_UI/components.json)):

| Field | Value | Consequence |
|-------|-------|-------------|
| `style` | `radix-luma` | base = **radix** → use `asChild` (never `render`); toasts via `sonner`. Luma = **component-owned spacing** |
| `iconLibrary` | `lucide` | import from `lucide-react` only |
| `rtl` | `true` | logical CSS only (`ms-*`/`me-*`/`start`/`end`), never `ml-*`/`left-*` |
| `aliases` | `@authsystem/ui`, `@authsystem/ui/utils`, `@authsystem/ui/hooks` | `cn` from `@authsystem/ui/utils`; never hardcode `@/components/ui/...` |
| `tailwind.css` | `apps/console/src/index.css` | edit this file for CSS variables; never create a new global CSS file |
| Preset | `b1tel7QNE` (supersedes `b1VlIzU8`) — see `README.md` › Stack | العرض بالكامل مملوك للـpreset — no custom colors, themes, or restyling. الاختيار الوحيد المسموح هو **أي control** يناسب الحالة |

**CLI OVERRIDE — يعلو على تعليمات المهارة (verified 2026-07-29):** every project-aware `shadcn` command (`info`, `docs`, `add`, `apply`) **fails** at the `Auth_UI/` root with `Could not resolve the following aliases: components, ui, lib` — the workspace aliases point at `@authsystem/ui`, which the CLI cannot map to a filesystem path. Therefore:

1. **Never run `shadcn add` for this repo.** Add a component by hand: write the canonical upstream source into `Auth_UI/packages/ui/src/<name>.tsx`, adapted to house conventions (`cn` from `@authsystem/ui/utils`, `data-slot`/`data-variant` attributes, `cva` variants) — mirror an existing sibling such as `badge.tsx` or `alert.tsx`. No `package.json` exports entry is needed; `@authsystem/ui/<name>` resolves automatically.
2. **Check `Auth_UI/packages/ui/src/` first** — it is the installed-component list; the skill's "injected project context" block is unavailable here.
3. **Fetch docs directly** at `https://ui.shadcn.com/docs/components/radix/<component>` (base = `radix`) instead of `shadcn docs`. `npx shadcn@latest docs <component>` works only from a directory **outside** the workspace, and defaults to the wrong base.

**قواعد غير قابلة للتفاوض (non-negotiable):** no custom colors/CSS/theme overrides; fix spacing at the component/primitive level (`FieldGroup` owns field gaps, `Field` owns label↔control gap) — never `space-y-*` / per-usage `gap-*` on a `<form>`; RTL-safe logical properties everywhere; `Dialog`/`Sheet`/`Drawer` always carry a Title.

---

## نموذج النطاق — ارتباطات `domain-driven-design` في هذا المستودع

**المهارة نفسها على مستوى المستخدم** وقواعدها عامّة؛ **ما يلي حقائق هذا المستودع وحده** (verified at `d846da3`, 2026-09-25). **المجلّد هو القائمة:** لا تنسخ قوائم الملفات إلى هنا — القائمة المنسوخة تنحرف بصمت، والمجلّد لا ينحرف.

| البند | الحقيقة في هذا المستودع | مصدر الحقيقة |
|-------|--------------------------|--------------|
| سلسلة القواعد | `EntityBase` ← `AuditableEntityBase` ← `AggregateRoot` | `Auth/Auth.Domain/Primitives/` |
| جذور التجميعات | كل صنفٍ يرث `AggregateRoot` — اليوم: `Application`، `NotificationLayout`، `NotificationTemplate`، `NotificationType`، `Organization`، `PendingRegistration`، `Permission`، `Role`، `User` | `grep -rlE ": *AggregateRoot" Auth/Auth.Domain/Entities` |
| حدود التجميع | `User` و`Organization` **لا يملكان مجموعاتٍ داخلية**: `UserRole`، `UserPermission`، `UserSession`، `UserExternalLogin`، `OrganizationUser`، `OrganizationInvitation`، `ApiKey` كياناتٌ مستقلّة لكلٍّ منها مستودعه. `Application` يملك `RedirectUris` وحدها | `Auth/Auth.Domain/Entities/` + `Auth/Auth.Domain/Interfaces/Repositories/` |
| مكان أحداث النطاق | `Auth/Auth.Domain/Events/` — **لا** `Auth.Application/Features` (خيار A في المهارة) | المجلّد |
| آلية الأحداث | `IDomainEvent : MediatR.INotification`؛ الجذر يجمعها بـ`RaiseDomainEvent`؛ `MediatRDomainEventDispatcher` ينسخها ويفرّغها ثم ينشرها، ويستدعيه المعالج بعد الحفظ. **ومسارٌ ثانٍ أكثر شيوعًا:** `IPublisher.Publish` مباشرةً من معالج الأمر — انظر قسم «الأحداث» | `Primitives/IDomainEvent.cs`، `Primitives/AggregateRoot.cs`، `Auth/Auth.Infrastructure/MediatRDomainEventDispatcher.cs` |
| اعتماديات `Auth.Domain` | `ErrorOr` و`MediatR.Contracts` (واجهاتٌ فقط) — استثناءٌ قائم من «Domain لا يعتمد على شيء» | `Auth/Auth.Domain/Auth.Domain.csproj` |
| معالجات الأحداث | `INotificationHandler<…Event>` تعيش في `Auth/Auth_API/Modules/*`، وأغلبها في `Modules/AuditLog` باسم `{Name}AuditEventHandler` | `grep -rlE "INotificationHandler<\w+Event>" Auth/Auth_API` |
| أخطاء النطاق | صنفٌ ساكن لكل مفهوم: `{Concept}Errors` | `Auth/Auth.Domain/Errors/` |
| المستودعات | الواجهات في `Auth/Auth.Domain/Interfaces/Repositories/`؛ التنفيذ بـDapper في `Auth/Auth.Infrastructure/Persistence/` — **لا مشروع Persistence مستقلّ**، خلافًا لنموذج الطبقات الخمس في `clean-architecture-structure` | المجلّدان |
| التقسيم | **لا خريطة سياقاتٍ محدودة موثّقة.** الكود مقسّم بوحدات `Auth/Auth.Application/Features/*`، والمخطط بستّ مجموعات جداول `Auth/Auth_DB/dbo/Tables/*` (`Authentication`، `Core`، `Notifications`، `Organizations`، `Security`، `System`)، والتقسيمان لا يتطابقان | المجلّدان |

**انحرافٌ مُصحَّح (2026-09-25):** كانت المهارة العامّة تحمل «حقائق» عن هذا المستودع: الأحداث في `Application/Features`، و`User` يملك أدواره وجلساته ودخوله الخارجي، و`Application` يملك مفاتيح API، وستة سياقات (`Identity`، `Authorization`، …) لا تطابق الكود. **كلّها خاطئة اليوم** وأُسقطت. النسخة المورَّدة في `.github/skills/domain-driven-design/SKILL.md` ما زالت تحملها — لا تعتمد عليها في هذه النقاط.

---

## الأحداث — ارتباطات `event-driven-architecture` في هذا المستودع

**المهارة نفسها على مستوى المستخدم** وقواعدها عامّة؛ **ما يلي حقائق هذا المستودع وحده** (verified at `d846da34`, 2026-09-25). **المجلّد هو القائمة:** لا جرد للأحداث ولا للمعالجات هنا — الجرد المنسوخ ينحرف بصمت.

| البند | الحقيقة في هذا المستودع | مصدر الحقيقة |
|-------|--------------------------|--------------|
| مرحلة التطوّر | **المرحلة الأولى:** أحداث النطاق تُنشر داخل العملية عبر MediatR، متزامنةً داخل الطلب. لا outbox للأحداث ولا وسيط رسائل. الانتقال إلى مرحلةٍ تالية قرارٌ يُسجَّل في ADR | تسجيل `AddMediatR` في `Auth/Auth_API/Program.cs` |
| الأحداث | سجلّات `record … : IDomainEvent` في مجلّدٍ واحد (45 اليوم) | `git ls-files Auth/Auth.Domain/Events` |
| مسارا النشر | (1) الجذر يرفع بـ`RaiseDomainEvent`، ثم يستدعي معالجُ الأمر `IDomainEventDispatcher.DispatchEventsAsync` بعد الحفظ. (2) معالج الأمر ينشر مباشرةً بـ`IPublisher.Publish` بعد الحفظ. **الثاني هو الأغلب** اليوم (نحو 40 استدعاءً في 38 ملفًّا، مقابل 15 `RaiseDomainEvent` في 4 ملفات). الحدث نفسه قد يأتي من المسارين: `RoleAssignedEvent` يُنشر مباشرةً من `AssignRoleCommandHandler` ومن `GrantApplicationAccessCommandHandler` | `grep -rn "RaiseDomainEvent(" Auth/Auth.Domain` · `grep -rn "_publisher.Publish" Auth/Auth.Application` |
| المستهلكون | ملفات `INotificationHandler<…Event>` تحت `Auth/Auth_API/Modules/*/EventHandlers` (53 اليوم). أغلبها في `AuditLog` باسم `{Event}AuditEventHandler`، **معالجٌ لكل حدث** لا `AuditEventHandler` واحد، والباقي في `Authentication` و`UserManagement` و`OrganizationManagement`. لا معالج في `API_Gateway` | `grep -rlE "INotificationHandler<" Auth/Auth_API/Modules` |
| إخفاق المعالج | لا ناشر إشعارات مخصّص، فالمعتمد ناشر MediatR الافتراضي: يمرّ على المعالجات بالتتابع، ويصعد استثناؤها إلى المُنادي. ولا يلتقط أي معالج استثناءه (53 من 53 بلا `catch`). **فاستثناءُ معالجٍ يصل إلى الأمر بعد أن حُفظت الحالة.** هذا انحرافٌ قائم عن قاعدة المهارة «لا يرمي المعالج»، مسجَّلٌ هنا لا مُصحَّح | كتلة `AddMediatR` في `Auth/Auth_API/Program.cs:697-702` + المجلّد |
| عقد الحدث | **لا يطابق قواعد العقد في المهارة:** لا حدث يحمل `TriggeredBy` (0 من 45)، وحدثٌ واحد فقط يحمل طابعًا زمنيًّا (`SessionLimitEnforcedEvent.OccurredAtUtc`). اسم الفاعل يتغيّر من حدثٍ لآخر (`CreatedBy`، `AssignedBy`، `modifiedBy`…) أو يغيب (`UserLoggedInEvent`). لا تفترض الحقلين في معالجٍ جديد | `Auth/Auth.Domain/Events/` |
| أحداث التكامل | **مُهيكلة وغير منشورة:** `IntegrationEvent` و`IIntegrationEventPublisher` وثلاثة عقود (`UserRegistered…`، `PasswordChanged…`، `RoleAssigned…IntegrationEvent`). المسجَّل هو `NoOpIntegrationEventPublisher` الذي يسجّل الحدث ويُسقطه، ولا موضع في الكود ينشئ أيًّا من العقود الثلاثة | `Auth/Auth.Application/IntegrationEvents/` · `Auth/Auth.Infrastructure/IntegrationEvents/NoOpIntegrationEventPublisher.cs` · `Auth/Auth_API/Program.cs:675-677` |
| الـoutbox الموجود | `NotificationOutbox` صندوق **تسليم إشعارات** (بريد/SMS/Push)، محتواه يُصيَّر عند الإدراج، وتسليمه at-least-once عبر `NotificationOutboxDispatcher`. **ليس outbox أحداث**، ولا جدول `OutboxMessages` | `Auth/Auth_DB/dbo/Tables/Notifications/NotificationOutbox.sql` · `Auth/Auth.Infrastructure/Notifications/Outbox/NotificationOutboxDispatcher.cs:13-20` |
| المضيفات | مضيفان: `Auth_API` يملك الحالة والأحداث، و`API_Gateway` لا يستهلك أحداثًا. فالنظام «خدمة واحدة» من جهة الأحداث | `Auth/Auth.sln` |

**انحرافٌ مُصحَّح (2026-09-25):** كانت المهارة العامّة تحمل «جرد أحداث حاليّة» من خمسة أحداث، يستهلكها كلَّها `AuditEventHandler` واحد، وتحمل كذلك «المرحلة الحالية: خدمة واحدة». **الجرد خاطئ اليوم:** يغطّي 5 أحداث من 45، ولا يوجد معالجٌ بذلك الاسم. أمّا المرحلة فصحيحة، وقد نُقلت إلى هنا. النسختان المورَّدتان في `.github/skills/event-driven-architecture/SKILL.md` (السطور 17 و202-212 و222) و`.agents/skills/event-driven-architecture/SKILL.md` (السطر 17) ما زالتا تحملان ذلك — لا تعتمد عليهما في هذه النقاط.

---

## الواجهة — ارتباطات `frontend-playbook` في هذا المستودع

**المهارة نفسها على مستوى المستخدم** وعامّة؛ **ما يلي حقائق `Auth_UI/` و`Auth/` التي كانت المهارة ترويها كأنها عامّة** (verified at `d846da34`, 2026-09-25). المسارات نسبيّةٌ إلى `Auth_UI/` ما لم تبدأ بـ`Auth/`. **الأرقام المقيسة لا تُنسخ إلى هنا:** مكانها الاختبار الذي يقيسها.

### عقد الـAPI (يقابل `rules/api-and-state.md` و`rules/tables.md`)

| البند | الحقيقة في هذا المستودع | مصدر الحقيقة |
|-------|--------------------------|--------------|
| اتجاه الفرز | `SortDirection` في مخطط OpenAPI رقمٌ: `Asc = 0` و`Desc = 1`، والرابط يقبل الاسم أيضًا. `toSortParams` يرسل الرقم | `Auth/Auth.Domain/Enums/SortDirection.cs:11-16` · `packages/api/src/helpers.ts:30-44` |
| الـenum في الأجسام | يُسلسَل **باسم العضو** (`"Active"`) عبر `JsonStringEnumConverter` عامّ. لا تناقض مع البند السابق: الاسم في الأجسام، والرقم في معامل الاستعلام. دوال الميتا (`userStatusMeta`) تقبل الاسم والرقم | `Auth/Auth_API/Program.cs:722` · `packages/ui/src/format.ts:129` |
| سقف `pageSize` | من 1 إلى 100، وما يتجاوزه يُرفض بـ400 (`Validation.PageSize.Range`) | `Auth/Auth.Application/Validators/Rules/SharedValidationRules.cs:94` (`IsValidPageSize`) |
| قائمة `sortBy` | مغلقة لكل استعلام، وما خارجها 400 | `Auth/Auth.Domain/Constants/SortFields.cs` · `SharedValidationRules.cs:171` (`IsValidSortField`) |
| التصدير الكامل | `collectAllPages` يمشي بصفحات `EXPORT_PAGE_SIZE = 100` وبسقف `EXPORT_MAX_ROWS = 50_000` | `packages/api/src/helpers.ts:47-49` |
| الأعداد | المخطط المولَّد يكتبها `number \| string` (مثل `totalCount`)، و`toNumber` يوحّدها | `packages/api/src/schema.d.ts` · `packages/api/src/helpers.ts:24-28` |
| شكل الخطأ | `ProblemDetails` **بلا عضو `code`**: الكود في `title` إن كان الخطأ واحدًا، وفي `errors[].code` (مع `description`) إن كان أكثر. فشل DataAnnotations في رابط النماذج يُرسل قاموس `{ field: string[] }`. `errors.ts` يقرأ الأشكال الثلاثة | `Auth/Auth_API/Common/ApiController.cs:42-57` · `packages/api/src/errors.ts:35,261,275-279` |
| كودٌ مثال | `User.EmailNotConfirmed` (403) | `Auth/Auth.Domain/Errors/UserErrors.cs:98-99` |
| الصلاحيات | السمة `[RequirePermission("…")]` (نحو 140 استعمالًا في `Auth_API`). خريطة الواجهة `PERMISSIONS` في `apps/console/src/lib/permissions.ts`، **ويحرس تطابقها** مع `Auth/Auth.Domain/Constants/PermissionCodes.cs` اختبارٌ يقرأ ملفّ C# نفسه | `Auth/Auth.Sdk/Authorization/RequirePermissionAttribute.cs` · `apps/console/src/lib/permissions.test.ts:9-27` |

### الجلسة (يقابل `rules/session.md`)

| البند | الحقيقة في هذا المستودع | مصدر الحقيقة |
|-------|--------------------------|--------------|
| عمر رمز الوصول | 15 دقيقة افتراضيًّا، ويُعدَّل وقت التشغيل ضمن 1–1440 | `Auth/Auth.Application/Configuration/JwtSettings.cs:23` · `Auth/Auth_API/appsettings.json:22` · `Auth/Auth.Application/SystemSettings/SystemSettingsRegistry.cs:40` |
| تخزين الرموز | رمز الوصول في الذاكرة، ويُبثّ إلى التبويبات عبر `BroadcastChannel`. رمز التحديث في `localStorage` بالمفتاح `auth.refreshToken`، لأن الـAPI يعيد الرموز في الجسم لا في كوكي HttpOnly. **قرارٌ موثّق في الملف:** إن نصّت المهارة العامّة على كوكي HttpOnly فهذا المستودع استثناءٌ قائم | `packages/api/src/token-store.ts:4-23` |
| قفل التحديث عبر التبويبات | Web Locks باسم `auth.refresh`، مع `BroadcastChannel` | `packages/api/src/tab-sync.ts:40,182-190,268` |
| الرفع خارج الـmiddleware | `upload.ts` يستدعي `ensureFreshAccessToken()` قبل الإرسال، ويعيد المحاولة مرّةً واحدة عند 401 عبر `sharedRefresh` | `packages/api/src/upload.ts:1,37-44` |
| الخروج يحذف المخزن | `resetUserScopedCache` | `packages/api/src/query.ts:72` |
| وجهة ما بعد الدخول | `useLoginCompletion` | `packages/auth/src/login-completion.ts:60` |

### الجداول واللغة والوقت (يقابل `rules/tables.md` و`rules/i18n-rtl.md`)

| البند | الحقيقة في هذا المستودع | مصدر الحقيقة |
|-------|--------------------------|--------------|
| تفضيلات الجدول | المفتاح المحلّي `dt:{scope}:{tableId}`، والـscope هو المستخدم. المزامنة إلى الخادم تحت البادئة `table:` بتأخير 800 ms، وعلم الهجرة `dt:migrated:v1` | `packages/ui/src/data-table/storage.ts:16,19,28,57` |
| اللغات | سبع لغات: `en/ar/tr/fr/zh/ur/fa`، في الحزمة `packages/i18n`. التكافؤ بينها يحرسه `locales.test.ts` | `packages/i18n/src/locales/locales.test.ts:3-20` |
| المنطقة الزمنية | العمود `Users.TimeZone` معرَّف `NOT NULL DEFAULT N'UTC'`، والقيمة `"UTC"` تُقرأ «تلقائي» (منطقة المتصفّح). UTC الحرفي هو `"Etc/UTC"`. **تحفّظ:** المُنتقي يبني قائمته من `Intl.supportedValuesOf("timeZone")`، وفي V8 (Node v22.18) لا تحوي القائمة `UTC` ولا `Etc/UTC`، فقد لا يمكن اختيار UTC الحرفي منها. لم يُتحقَّق من ذلك في متصفّح | `Auth/Auth_DB/dbo/Tables/Core/Users.sql:21` · `packages/i18n/src/timezone.ts:6-8,29-31` · `packages/ui/src/common/timezone-select.tsx:43` |

### التسليم والاختبار والوصولية (يقابل `rules/delivery.md` و`rules/testing.md` و`rules/accessibility.md`)

| البند | الحقيقة في هذا المستودع | مصدر الحقيقة |
|-------|--------------------------|--------------|
| تقسيم المسارات | كل شاشة خلف `lazyRoute`. تبويبات لوحة القيادة الخمسة (`overview` و`security` و`people` و`apps` و`audit`) مكوّنات `React.lazy` | `packages/ui/src/lazy-route.tsx:32` · `apps/console/src/pages/dashboard/tabs/lazy-tabs.tsx:17-21` |
| قطع المكتبات | `vendor-chunks.ts` في جذر `Auth_UI/`، يشترك فيه التطبيقان عبر `manualChunks` | `vendor-chunks.ts` · `apps/console/vite.config.ts:39` · `apps/accounts/vite.config.ts:45` |
| عتبة التحذير | `chunkSizeWarningLimit: 400` في التطبيقين | `apps/console/vite.config.ts:33` · `apps/accounts/vite.config.ts:40` |
| الاستضافة | IIS: لكل تطبيق `web.config` يُختم بـ`scripts/seal-web-config.mjs` | `apps/console/public/web.config` · `apps/accounts/public/web.config` |
| ميزانيات الحمولة | الأرقام المقيسة والميزانيات في `login-payload.spec.ts` وحده | `e2e/isolated/login-payload.spec.ts` |
| pnpm | `publicHoistPattern: [zod]`، لأن `@hookform/resolvers` يستورد `zod` دون أن يصرّح به | `pnpm-workspace.yaml:5-8` |
| Playwright المعزول | `playwright.isolated.config.ts` بمشروعين. محاكاة الـAPI للوحة في `e2e/isolated/mock-authenticated-api.ts` (`installAuthenticatedApi`)، وللحسابات في `e2e/isolated/accounts/mock-anonymous-api.ts` (`installAnonymousApi`) | `playwright.isolated.config.ts:3-12` |
| قياس الفيض | يُقاس على الإطار لا على المستند: عنصرٌ بعرض 3000px يُبقي قراءة المستند صفرًا، وقراءة الإطار 2625 | `e2e/isolated/layout-overflow.ts:9-10` |
| استثناءات التباين | `KNOWN_PALETTE_GAPS` مربوطة بزوج الألوان لا بالمُحدِّدات: `--muted-foreground` على `--muted` يقيس 4.34:1، و`--destructive` على صبغته يقيس 4.0:1. القيم من `packages/ui/src/preset.css` | `e2e/isolated/accessibility.spec.ts:24-49` |

**انحرافٌ مُصحَّح (2026-09-25):** كانت المهارة العامّة تروي ما في هذا القسم كأنه صفة «الخلفية» عمومًا. ومنه ما لم يعد صحيحًا هنا أيضًا:
- «خريطة `PERMISSIONS` مُحاكاة يدويًّا في `constants.ts` بلا حارس»: الخريطة اليوم في `permissions.ts`، ويحرسها `permissions.test.ts`.
- «هبط `/` من 1,371,835 إلى 959,442 بايت»: الاختبار يسجّل اليوم قيمةً أخرى. **لا تعتمد على الأرقام المنقولة في المهارة**، فالمصدر هو الاختبار.
