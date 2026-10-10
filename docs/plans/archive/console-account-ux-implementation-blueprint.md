<div dir="rtl" lang="ar">

# Blueprint تنفيذ تحسينات Console وAccount

## حالة التنفيذ

- المصدر التحليلي: `docs/reviews/console-account-ux-audit.md`.
- النطاق المعتمد: `IMP-01` حتى `IMP-10` دون تغيير ترتيب الاعتماد.
- **المنفذ: العشر مراحل جميعًا.** `IMP-01` حتى `IMP-09` مغلقة ببواباتها؛ `IMP-10` مغلقة عدا جلسة Usability التي تحتاج شخصًا لا اختبارًا.
- بصمة التحقق الحالية: `910/910` اختبار وحدة، `55/55` Playwright معزول، `pnpm lint` و`pnpm typecheck` والبناء الإنتاجي للتطبيقين ناجحة.
- **بوابة الأسطر المتغيرة مُجتازة: `90.35%` (`740/819`).** الرقم السابق `98.31%` كان ناتج قياس متضخّم صُحِّح ضمن هذه الجولة، فهبط إلى `84.99%` ثم رُفع بالعمل لا بالتخفيف. الهامش سطران فقط فوق الحدّ الأدنى، ولاحظ أن قاعدة القياس تنقلب من `HEAD` إلى `HEAD^` لحظة الالتزام.
- تحقق Runtime: حزمة Playwright معزولة تبني Console بإعداد Production وتستبدل حدود API فقط؛ لا تعتمد على Production أو حساب إداري دائم.
- قاعدة الانتقال: لا تبدأ مرحلة تالية قبل نجاح اختبارات المرحلة الحالية وتحقق Exit Criteria الخاصة بها.

### ما غيّرته المراجعة الخصومية في هذه الخطة نفسها

| ما نصّت عليه الخطة | ما نُفِّذ بدلًا منه | السبب |
|---|---|---|
| بناء المسارات على `href()` من React Router «لضمان ترميز المعرفات» | `routePath` — tagged template يُرمّز كل قيمة مُدرَجة | `href()` **لا يُرمّز**؛ يستبدل القيمة الخام في النمط. مثبت باختبار: `href("/users/:id", {id: "a/b"})` يُنتج `/users/a/b` |
| «عزل DataTable» بوصفه الاختناق الأول | حارس مركزي، لكنه وقائي لا علاجي | كل جدول في المستودع يمرّر `enableRowDetail={false}`؛ التصادم لم يكن قائمًا. أُضيف الحارس لأن السلوك الافتراضي للمكوّن `true` |
| `TabsTrigger asChild + Link` لشريط الإشعارات | `<nav>` فيه روابط، بأصناف Tabs نفسها | الشريط كان يُصدر `role="tab"` مع `aria-controls` يشير إلى لوحة **غير موجودة**، وتفعيله التلقائي يجعل سهم لوحة المفاتيح يغيّر المسار في كل ضغطة |
| «لا مكوّن `RecordLink` عام» | `RecordLink` موجود | ما يقرره كل خلية واحد: صفٌّ بلا وجهة يُعرض نصًا. اثنتان وعشرون نسخة من الشرط نفسه ليست وضوحًا |
| Preview يتبع `resolvedTheme` | `usePreviewScheme`: بذرة أولى من السمة، ثم اختيار محفوظ | الربط الدائم بالسمة يعيد المشكلة التي وثّقها الكود: نسختان مختلفتان لقالب واحد. الحفظ يعالج الشكوى الفعلية — نقرة إضافية في كل فتح — دون ذلك الثمن |

## 1. النواة التشغيلية المطلقة (Absolute MVP)

أصغر تغيير مستقل يزيل أعلى مخاطرة تشغيلية بلا اعتماد على مهام أخرى هو تأمين Publish/Unpublish للقوالب والتخطيطات:

1. تحويل الأمر المباشر إلى آلة حالات صريحة: `Idle → Armed → Pending → Succeeded | Failed`.
2. عرض ملخص ثابت قبل التنفيذ: العنصر، النسخة/المراجعة المحفوظة، والنطاق.
3. ربط طلب الخادم بالنسخة التي عُرضت في التأكيد بقفل تفاؤلي؛ لا يكفي عرض رقم نسخة في الواجهة إذا كان الخادم قد ينشر مسودة أحدث.
4. منع طلب ثانٍ أثناء `Pending`، ومنع إغلاق الحوار حتى انتهاء الطلب.
5. عند النجاح: إغلاق الحوار، تحديث Query Cache، ثم إظهار نجاح.
6. عند الفشل: إبقاء الحوار مفتوحًا، عدم تغيير الواجهة محليًا، وإظهار الخطأ.

حدود MVP:

- يشمل Template publish وTemplate unpublish وLayout publish.
- لا يضيف Unpublish للتخطيطات لأن عقد المجال الحالي لا يوفّره.
- Template يعرّف النسخة بـ`DraftVersionId` أو `PublishedVersionId`.
- Layout غير versioned؛ هويته المؤكدة هي `ModifiedAt ?? CreatedAt`، وتظهر للمستخدم بوصفها آخر مسودة محفوظة.
- لا يغيّر نموذج التخزين أو يضيف dependency.

## 2. المعمارية الطوبولوجية النصية (Text-Topology)

```text
[Admin]
   │ click
   ▼
[Template/Layout Detail Page]
   │ captures immutable publish target
   ├──────────────► [Publish Confirmation Summary]
   │                         │ item/version/scope
   │                         ▼
   └────────────────► [ConfirmDialog / AlertDialog]
                             │ confirm once
                             ▼
                    [TanStack Mutation: Pending]
                             │ centralized typed client
                             ▼
                    [Console HTTP API client]
                             │ JSON request + auth
                             ▼
                    [ASP.NET API Controller]
                             │ ISender.Send(command)
                             ▼
                    [MediatR Command Handler]
                             │ loads aggregate
                             ▼
                    [Notification Aggregate]
                             │ compare expected revision
                    ┌────────┴────────┐
                    │ match           │ mismatch
                    ▼                 ▼
              [Publish state]   [ErrorOr Conflict]
                    │                 │
                    ▼                 ▼
             [Repository write] [RFC 7807 / 409]
                    │
                    ├──► [Cache invalidation]
                    └──► [Existing domain events/logging]
                             │
                             ▼
                    [Query invalidation + toast]
```

العلاقات المقيدة:

- UI يعتمد على API schema المولّد، ولا يستورد أي طبقة خادم.
- API يحول HTTP contract إلى Command فقط؛ لا يحتوي قواعد النشر.
- Application ينسق التحميل والرندر والحفظ.
- Domain aggregate وحده يقرر هل النسخة المتوقعة ما زالت صالحة للنشر.
- Infrastructure ينفذ الحفظ وإبطال cache ولا يقرر قواعد العمل.

## 3. مسار انسياب البيانات (Data Flow Pipeline)

1. يجلب React Query سجل Template أو Layout ويحفظ revision identifiers في cache.
2. يضغط المدير Publish/Unpublish؛ لا يرسل أي طلب.
3. تحفظ الصفحة هدف العملية المعروض وتفتح `ConfirmDialog`.
4. يعرض الحوار الاسم والنسخة/وقت المراجعة والنطاق من الهدف المحفوظ.
5. Cancel يعيد الحالة إلى `Idle` دون mutation أو invalidation.
6. Confirm ينتقل إلى `Pending` ويرسل revision identifier مع الطلب.
7. Controller ينشئ Command typed ويمرر `CancellationToken`.
8. Handler يحمل aggregate ويستدعي behavior واحدًا مع revision المتوقع.
9. Aggregate يقارن المتوقع بالحالي قبل أي تغيير:
   - التطابق: ينفذ الانتقال وينشئ الأحداث الموجودة.
   - الاختلاف: يعيد `ErrorOr.Conflict` ولا يغير الحالة.
10. بعد نجاح الحفظ فقط تُبطل caches الخاصة بمسار الإرسال.
11. الواجهة عند النجاح تغلق الحوار، تبطل detail/list queries، وتعرض toast.
12. الواجهة عند الخطأ تبقي الحوار مفتوحًا؛ 409 يفرض reload/review قبل محاولة جديدة.

## 4. حزمة التقنيات المحسوبة (Tech Stack)

| الحاجة | التقنية الموجودة والمختارة | تكلفة الإضافة | سبب الكفاءة |
|---|---|---:|---|
| حالة الخادم والـsingle-flight | TanStack React Query 5 | صفر | mutation state وcache invalidation موجودان بالفعل. |
| تأكيد حساس accessible | shadcn/Radix `AlertDialog` عبر `ConfirmDialog` | صفر | focus trap وkeyboard semantics وعقد محلي مجرّب. |
| HTTP typed | `openapi-fetch` client و`schema.d.ts` | صفر | يمنع اختلاف body بين الواجهة والخادم. |
| API | ASP.NET Core Controller + RFC 7807 | صفر | البنية الحالية وتحويل ErrorOr إلى 409 موجودان. |
| Use case | MediatR Command/Handler | صفر | يحافظ على CQRS واتجاه الاعتماد. |
| invariant | Aggregate behavior + ErrorOr | صفر | يمنع تجاوز القفل من مستهلك آخر داخل النظام. |
| الاختبارات | xUnit/Moq + Vitest/Testing Library + Playwright | صفر | يغطي المجال والعقد والتفاعل بنفس أدوات المستودع. |
| الترجمة | i18next وكتالوج اللغات السبع | صفر | parity وplaceholder tests موجودة. |

الترشيح يرفض إضافة state library أو dialog library أو endpoint موازٍ؛ كل حاجة مغطاة بعقود موجودة، وأي dependency جديدة تزيد مساحة الفشل دون عائد.

## 5. آلة الحالات الحتمية

| الحالة | المدخل المسموح | الأثر | الانتقال |
|---|---|---|---|
| `Idle` | ضغط Publish/Unpublish المصرح | التقاط الهدف؛ لا شبكة | `Armed` |
| `Armed` | Cancel | مسح الهدف | `Idle` |
| `Armed` | Confirm | mutation واحدة وتعطيل المخارج | `Pending` |
| `Pending` | أي ضغط إضافي/إغلاق | مرفوض | `Pending` |
| `Pending` | 2xx | إغلاق، invalidation، toast | `Succeeded → Idle` |
| `Pending` | 409 revision conflict | إبقاء الحوار وإظهار تعارض قابل للاسترداد | `Failed` |
| `Pending` | 4xx/5xx/network | إبقاء الحوار وإظهار خطأ، دون optimistic state | `Failed` |
| `Failed` | Cancel | مسح الهدف | `Idle` |
| `Failed` | Confirm بعد تصحيح/إعادة تحميل | mutation جديدة | `Pending` |

ثوابت لا يجوز كسرها:

- لا mutation من `Idle`.
- لا أكثر من mutation واحدة فعالة للعملية نفسها.
- لا نجاح بصري قبل نجاح الخادم.
- لا نشر لrevision مختلف عن المعروض.
- authorization في الخادم إلزامي حتى لو أخفت الواجهة الزر.

## 6. خوارزمية البناء المتسلسلة

### المسار الكلي

| Gate | المهمة | شرط الدخول | شرط الخروج |
|---:|---|---|---|
| 1 | `IMP-01` Publish safety | لا شيء | Confirm + revision lock + tests خضراء. |
| 2 | `IMP-02` Permission-aware IA | Gate 1 | Route/Nav/Tabs/Search تقرأ metadata واحدة ومصفوفة الأدوار تمر. |
| 3 | `IMP-03` Global Search start state | Gate 2 | Recent وJump to مستقلان في الحالات الثلاث. |
| 4 | `IMP-04` URL list state | Gate 3 | كل قائمة paginated تعيد الحالة بعد Back/Reload/deep link. |
| 5 | `IMP-05` Unsaved editors | Gate 4 | كل editor يحذر ويصفّر dirty بعد الحفظ فقط. |
| 6 | `IMP-06` Action discoverability | Gate 5 | كل action مسموح ظاهر، مع fallback responsive مسمى. |
| 7 | `IMP-07` Recoverable errors | Gate 6 | code/status mapping وfield/retry paths مغطاة. |
| 8 | `IMP-08` Link semantics | Gate 7 | كل route destination يملك href ولا mutation داخل Link. |
| 9 | `IMP-09` Preview scheme | Gate 8 | default من resolvedTheme مرة واحدة والاختيار اليدوي ثابت. |
| 10 | `IMP-10` Regression/usability | Gate 9 | lint/typecheck/tests/E2E/RTL/viewports وجلسة usability موثقة. |

### خوارزمية `IMP-01`

1. تعريف revision contract في API requests.
2. فرض revision invariant داخل aggregates.
3. تمرير العقد عبر Commands وControllers.
4. تحديث OpenAPI TypeScript schema.
5. بناء ملخص تأكيد مشترك.
6. توصيل Template publish/unpublish.
7. توصيل Layout publish.
8. إضافة نصوص اللغات السبع مع placeholder parity.
9. اختبار Domain conflict/success.
10. اختبار Application: لا حفظ ولا cache invalidation عند conflict.
11. اختبار UI: لا request قبل confirm؛ Cancel صفر requests؛ Pending يمنع التكرار؛ success/error transitions صحيحة.
12. تشغيل gates بالترتيب: targeted tests → typecheck → full tests → lint/build → E2E الآمن إن توفرت fixtures.

لا يجوز تقديم خطوة على سابقتها؛ schema والواجهة لا يتقدمان على invariant الخادم.

### خوارزمية `IMP-02` المنفذة

1. فصل قاموس الصلاحيات عن `constants.ts` لمنع دورة اعتماد بين بنية التنقل وmetadata الوجهات.
2. إنشاء سجل واحد typed لوجهات Notifications يربط `route + permission + tab + search`.
3. جعل مسار `notifications` الأب حاوية فراغية فقط، وتطبيق guard مستقل من السجل على كل فرع.
4. حسم `/notifications` حتميًا: Overview لمن يقرأ القوالب، Policy لمن يقرأ السياسة فقط، و403 لمن لا يملك أيًا منهما.
5. اشتقاق Sidebar وTabs وGlobal Search من السجل نفسه؛ لا توجد قوائم صلاحيات موازية.
6. إبقاء legacy redirects وbreadcrumb الأب على عقودهما السابقة واختبارهما في المتصفح.
7. تشغيل مصفوفة الأدوار الأربع: `privacy-only | templates-only | both | neither` على Route/Nav/Tabs/Search.
8. اختبار المسارات المباشرة للفرعين، وحجبها بـ403 عند غياب صلاحيتها الخاصة.

ثوابت `IMP-02`:

- امتلاك `privacy-policy:read` لا يمنح أي فرع من فروع القوالب.
- امتلاك `notification-templates:read` لا يمنح فرع Policy.
- العنصر المخفي في Sidebar/Tabs/Search يبقى محميًا في Route؛ الإخفاء ليس authorization.
- لا يختار الأب صلاحية جامعة وهمية؛ كل child يملك authority واحدة معلنة في metadata.

### خوارزمية `IMP-03` المنفذة

1. فصل مجموعتي الخمول: `Recent` اختيارية و`Jump to` ثابتة بدل شرط `either/or`.
2. إظهار `Separator` فقط عندما توجد صفوف Recent فعلية.
3. حصر Clear في storage الخاص بالحساب الحالي؛ لا يمس فهرس التنقل السريع.
4. إبقاء `Jump to` مشتقًا من فهرس الوجهات المفلتر بالصلاحيات؛ لا تظهر وجهة غير مأذونة في حالة البدء.
5. تثبيت الحالات الثلاث باختبارات تفاعل: `no history | history | cleared history`، مع ترتيب المجموعات وفتح الوجهة.
6. تثبيت EN وAR/RTL في Playwright على build إنتاجي وAPI boundaries معزولة.

ثوابت `IMP-03`:

- وجود عنصر Recent واحد أو أكثر لا يخفي `Jump to`.
- Clear لا يزيل `Jump to` ولا يغير صلاحياته.
- `Recent` يسبق `Separator` ثم `Jump to` في LTR وRTL؛ الاتجاه لا يعكس الترتيب الدلالي.
- سجل حساب لا يظهر لحساب آخر، والسجل التالف يسقط إلى مصفوفة فارغة دون تعطيل البحث.

### خوارزمية `IMP-08` المنفذة

1. `routePath` في `packages/ui`: tagged template يُرمّز كل قيمة مُدرَجة. الآلية وحدها هنا؛ معرفة المسارات تبقى عند التطبيق.
2. خريطة واحدة لكل تطبيق: `apps/console/src/lib/record-hrefs.ts` لثمانية أنواع سجلات، و`packages/account/src/lib/record-hrefs.ts` للمنظمات — النوع الوحيد الذي يركّبه التطبيقان على المسار نفسه.
3. كل باني يقبل المعرّف كما تسلّمه أنواع الـAPI المولّدة (اختياريًا) ويُجيب `undefined` عند غيابه. هذا ما يمنع رابطًا إلى `/users/undefined` يفشل بعد النقرة لا قبلها.
4. `RecordLink` يقرر مرة واحدة ما يحدث حين لا توجد وجهة: المحتوى نفسه نصًا، بالأصناف نفسها، فلا يتزحزح العمود.
5. حارس مركزي في `DataTable`: نقرة الصف تتنحّى عن أي عنصر تفاعلي بداخله.
6. شريط أقسام الإشعارات صار `<nav>` فيه روابط، بأصناف `tabsListVariants` و`tabsTriggerVariants` نفسها — الشكل ثابت، والدلالة صارت صادقة.
7. تحويلات ما بعد الإنشاء والحذف بقيت `navigate()` — انتقال سير عمل، لا عنوان نقر عليه أحد — لكنها تبني مسارها عبر البانين أنفسهم.

ثوابت `IMP-08`:

- إن كانت النتيجة عنوانًا يمكن نسخه أو مشاركته فالعنصر رابط؛ وإن كانت mutation أو حوارًا فالعنصر زر.
- لا `preventDefault` ولا `navigate()` على رابط: Ctrl/⌘ والنقرة الوسطى وقائمة السياق تبقى للمتصفح.
- الإخفاء ليس تفويضًا: الرابط ليس حاجزًا أمنيًا، وحارس المسار يبقى السلطة.
- صفٌّ بلا معرّف لا يصير رابطًا مكسورًا؛ يصير نصًا.

### خوارزمية `IMP-09` المنفذة

1. `usePreviewScheme` يقرأ تفضيلًا محفوظًا؛ فإن لم يوجد، يبذر من `resolvedTheme` مرة واحدة عند أول فتح.
2. القراءة تحدث في `useState` initializer، لا في `useEffect` مرتبط بالسمة: تغيير السمة لاحقًا — بما فيه اختصار «d» — لا يحرّك معاينة ضبطها المؤلف.
3. الاختيار اليدوي يُحفظ في `localStorage`؛ التخزين المحجوب لا يعطّل المعاينة، بل يعيدها إلى البذر في كل جلسة.
4. المعاينة التأليفية وسجل الإرسال يتقاسمان الـhook نفسه، فمن يحقق في شكوى يفتح المخطط الذي كان يعمل عليه.

ثوابت `IMP-09`:

- المخطط خاصية للمراجعة لا لشاشة المراجِع: قيمة صريحة ظاهرة على الشاشة، لا انعكاس صامت لإعداد آخر.
- الاختلاف بين شخصين ممكن في الفتحة الأولى وحدها، قبل أن يختار أيٌّ منهما — وهذا ثمن ألّا تفتح Console داكنة معاينة فاتحة.
- `color-scheme` على الـiframe يبقى الآلية؛ لا خلفية مُختلقة تُصادق إصلاح الوضع الداكن على كذبة.

## 7. مكونات التنفيذ

### Revision invariant

- **What:** مقارنة `ExpectedDraftVersionId`/`ExpectedPublishedVersionId` للقالب و`ExpectedRevisionAt` للتخطيط داخل aggregate.
- **Why:** يغلق نافذة TOCTOU ويجعل التأكيد صادقًا.
- **Alternatives rejected:** فحص الواجهة فقط لا يحمي من مدير آخر؛ فحص handler فقط يترك قاعدة العمل قابلة للتجاوز.
- **Trade-off:** طلبات العملاء القديمة بلا body تصبح غير صالحة؛ هذا مقصود لعملية حساسة ويستلزم نشر الواجهة والخادم كوحدة متوافقة.
- **Dependencies:** Domain errors؛ تستخدمه Commands.

### HTTP/CQRS contract

- **What:** request records صغيرة وCommands typed تحمل revision المتوقع.
- **Why:** body صريح قابل للتوليد والاختبار مع بقاء Controller thin.
- **Alternatives rejected:** query string أقل وضوحًا لعملية command؛ header مخصص يخفي المعنى عن OpenAPI.
- **Trade-off:** يتغير API contract، مقابل ضمان عدم نشر محتوى غير مؤكد.
- **Dependencies:** aggregates؛ يستخدمه typed client.

### Publish confirmation summary

- **What:** مكوّن مشترك يعرض item/version/scope داخل `ConfirmDialog`.
- **Why:** DRY واتساق Template/Layout وLTR/RTL.
- **Alternatives rejected:** markup مكرر في الصفحتين؛ toast بعد التنفيذ لا يمنع الخطأ.
- **Trade-off:** سطر إضافي في المسار، مقابل منع mutation العرضي.
- **Dependencies:** i18next وshadcn Item/AlertDialog؛ تستخدمه صفحتا التفاصيل.

### Mutation orchestration

- **What:** target state محلي وReact Query mutations غير optimistic.
- **Why:** state صغير ومحدد ولا يحتاج store عامًا.
- **Alternatives rejected:** global store يزيد الاقتران؛ browser confirm لا يعرض summary ولا يدعم تصميم اللغات.
- **Trade-off:** يتوقف الحوار أثناء الشبكة، مقابل منع إعادة الإرسال والغموض.
- **Dependencies:** typed API client؛ ينتج invalidation/toast.

## 8. المخاطر الخمس العليا واختبار الإجهاد

| المسار | الاحتمال | الأثر | أفضل حالة | أسوأ حالة | الأكثر ترجيحًا | التخفيف |
|---|---|---|---|---|---|---|
| TOCTOU بين العرض والتنفيذ | متوسط | كارثي | لا تعديل متزامن | نشر محتوى لم يره المدير | تعارض نادر في فرق الإدارة | revision invariant و409. |
| duplicate request | متوسط | عالٍ | الشبكة سريعة | حدثان/إبطالان ونتيجة ملتبسة | double-click أو latency | disabled pending + single-flight test. |
| إغلاق overlay أثناء pending | منخفض | عالٍ | نجاح فوري | طلب نجح والواجهة تبدو بلا نتيجة | escape أثناء شبكة بطيئة | تجاهل onOpenChange أثناء pending. |
| schema drift | متوسط | عالٍ | نشر ذري للعميل والخادم | frontend body لا يطابق API | typecheck يلتقطه قبل release | OpenAPI generation وcontract tests. |
| 409 غير قابل للاسترداد | متوسط | متوسط | reload واضح | retry على revision قديم بلا نهاية | المستخدم يعيد المحاولة | إبقاء dialog + رسالة reload/review؛ IMP-07 يعمم mapping. |

## 9. حالات الحافة النادرة عالية الأثر

| الحالة | الفشل المحتمل | الاستجابة الحتمية |
|---|---|---|
| حذف المسودة بعد فتح التأكيد | نشر لا شيء أو draft جديد | revision mismatch → 409، بلا write. |
| حفظ مدير آخر لنفس Layout | نشر HTML مختلف | timestamp mismatch → 409. |
| نشر Version أخرى ثم Unpublish من حوار قديم | إلغاء نسخة لم تكن معروضة | published version mismatch → 409. |
| ضغط Enter ثم click بسرعة | طلبان | أول حدث ينقل إلى Pending ويعطل confirm؛ assertion لطلب واحد. |
| Escape/overlay click أثناء Pending | حوار يختفي والطلب مستمر | تجاهل close حتى settle. |
| نجاح الخادم وفشل refetch | live state صحيحة وUI قديمة | نجاح واضح ثم query تبقى stale حتى retry؛ لا rollback وهمي. |
| timeout بعد commit | العميل لا يعرف هل نجح | لا auto-retry لعملية النشر؛ refetch أولًا لتحديد الحالة. |
| ترجمة طويلة/RTL | إخفاء القيمة أو عكس identifier | flex منطقي و`bdi` للقيم المحايدة واختبار catalog parity. |
| `ModifiedAt` فارغ في Layout جديد | revision غير محدد | استخدام `CreatedAt` كrevision أولي موثوق. |
| صلاحية سُحبت بعد فتح الحوار | UI ما زالت تعرض confirm | الخادم يعيد 403؛ لا mutation محلية. |

## 10. الاختناقات التشغيلية

| الاختناق | التشخيص | طريقة التجاوز |
|---|---|---|
| OpenAPI generation يعتمد على API محلي | schema قد يتأخر عن contract | بناء API أولًا ثم تشغيل مولد schema؛ يمنع patch اليدوي إلا إذا تعذر التشغيل ويوثق ذلك. |
| Layout بلا رقم Version | لا يمكن عرض رقم صادق | revision timestamp في العقد والواجهة؛ لا اختلاق version. |
| فحوص E2E المحمية بلا seeded admin | runtime coverage يتوقف | disposable tenant + IDs + mail sink؛ الاختبار يتخطى نفسه دونها ولا يستخدم Production. |
| lint baseline فيه مخالفات سابقة | gate العامة لا تعزل regression | lint للملفات المعدلة ثم gate كاملة؛ تسجيل baseline منفصل وعدم نسبه للتغيير. |
| invalidation متزامن لعدة queries | overlays قد تبقى أو UI تتذبذب | إغلاق state صريح واختبار overlay cleanup الموجود؛ invalidation بعد النجاح فقط. |
| رندر publish لكل لغة | latency يطيل Pending | progress غير ضروري في MVP؛ يبقى الحوار مقفلًا، ويقاس الزمن قبل أي تحسين backend. |

## 11. Checkpoints ومعايير القبول

### Phase A — Domain/API safety

- [x] النسخة المتوقعة جزء إلزامي من أوامر النشر/إلغاء النشر.
- [x] mismatch يعيد 409 ولا يستدعي repository update أو cache invalidation.
- [x] match يحافظ على events/logging/caching الحالية.
- [x] Controller وOpenAPI يعكسان body typed.

### Phase B — UI confirmation

- [x] لا POST قبل Confirm.
- [x] Cancel يغلق بلا POST.
- [x] الملخص يعرض item/version-or-revision/scope.
- [x] Pending يمنع confirm ثانيًا ويمنع إغلاق الحوار.
- [x] النجاح يغلق ويعيد التحقق؛ الفشل يبقي الحوار مفتوحًا.
- [x] Template publish/unpublish وLayout publish مغطاة.

### Phase C — Validation

- [x] Domain/Application tests تمر.
- [x] Vitest interaction tests وlocale parity تمر.
- [x] TypeScript و.NET builds يمران.
- [x] lint لا يضيف مخالفة جديدة.
- [x] E2E المعزول يمر على build إنتاجي، مع API boundaries حتمية وبدون Production credentials.

نتيجة التحقق المسجلة بعد Gate 3 في 2026-08-21:

- Backend: `1875/1875`؛ ومجموعة PFX المستقلة `5/5` بعد تحميل الشهادة بـ`EphemeralKeySet` لتجنب اعتماد Windows على user key store.
- Frontend: `647/647` عبر `70` ملف اختبار؛ وTypeScript typecheck وبناء Accounts/Console الإنتاجي ناجحان.
- E2E المعزول: `9/9`؛ أضيفت حالات IMP-03 الثلاث إلى Publish safety ومصفوفة IA، وتشمل العربية/RTL على build إنتاجي.
- تغطية الأسطر المتغيرة Frontend: `94.72%` (`915/966`) مقابل بوابة إلزامية `90%`. مجلد Global Search نفسه يحقق `90.00%` للأسطر.
- صُحح قياس Frontend العام من V8 إلى Istanbul: V8 كان يعطي ملفات غير مستوردة خرائط فارغة ويحذفها من المقام. القياس الصادق لكل المصدر هو Statements `35.59%`، Branches `28.48%`، Functions `28.15%`، Lines `35.51%`، مع ratchet مانع للانخفاض عند `35.5/28.4/28.1/35.5` على الترتيب.
- Backend العام يبقى عند `52.35%` مع floor `52.3%` وبوابة أسطر متغيرة `90%`؛ رفعه إلى 90% دين تاريخي مستقل، ولا تُستبعد repositories/controllers منه لرفع الرقم صوريًا.
- Lint العام أصبح `0 errors + 0 warnings` تحت `--max-warnings 0`؛ أُغلق baseline السابق `19 errors + 1 warning` بدل إعفائه.
- أُصلح مسار تقرير `test:coverage:changed` الذي كان يُحل من جذر خاطئ، وثُبت `@vitest/coverage-istanbul` على الإصدار المطابق لـVitest؛ لذلك البوابة الحالية تقرأ التقرير وتفشل فعليًا عند النزول عن 90%.

## 12. مبدأ التشغيل

لا تجعل التأكيد وصفًا لعملية لاحقة؛ اجعله عقدًا مرتبطًا بهوية الحالة التي شاهدها المستخدم، وارفض التنفيذ إذا تغيّرت تلك الهوية.

## 13. نتيجة المراجعة النهائية — العشر مراحل

| المحور | النتيجة | الدليل أو الفجوة |
|---|---|---|
| جودة الكود والمعمارية | متحقق | CQRS/MediatR/ErrorOr محفوظة؛ الآلية مفصولة عن السياسة في `routePath` مقابل خرائط المسارات، وفي `SORTABLE_COLUMNS` مقابل الصفحات. لا استيراد من طبقة إلى أخرى بالمقلوب. |
| الأمن | متحقق مع إصلاحين | صلاحيات endpoints لم تتغير. أُغلق تسريبان: نص استثناء الخادم في المعاينة، ونص `SqlException` عبر رموز `Secret.ConnectionString*`. قائمة النطاقات المسموحة تفشل مغلقة أمام رمز غير مسجَّل. |
| الاختبارات | متحقق جزئيًا | `887/887` وحدة و`21/21` Playwright. **تغطية الأسطر المتغيرة `84.99%` دون بوابة `90%`** بعد تصحيح القياس. ثلاثة اختبارات كانت غير قابلة للفشل أُعيدت كتابتها وثبت أنها تفشل عند تعطيل ما تحرسه. |
| الواجهة والوصولية | متحقق للنطاق | `aria-controls` المعلّق في شريط الإشعارات أُزيل بتحويله إلى `<nav>` مع `aria-current="page"`. `<p>` داخل `<button>` — HTML غير صالح — صار `<p>` داخل `<a>` وهو صالح. تدقيق WCAG الشامل ما زال غير منفَّذ. |
| الخلفية والفشل | متحقق | `unwrap` صار يفحص الحالة لا `error` وحده، فاستجابة فاشلة بجسم غير مقروء لم تعد تُعرض «لا نتائج». قوائم الفرز مربوطة بـ`SortFields.cs` باختبار عقد. |
| الوثائق والتشغيل | محدَّث وصادق | ملف المراجعة يحمل الآن سبب انخفاض الرقم، وقائمة العيوب المُغلقة، وما بقي مفتوحًا. هذا الملف يحمل ما غيّرته المراجعة في الخطة نفسها. |
| المبادئ العشرة | لا مخالفة مكتشفة | Clean Architecture وDDD وSOLID وOOP وCQRS وMediatR وErrorOr وEDA وDRY محفوظة؛ Strategy غير منطبق. |

### ما لا يُعلن مكتملًا

بوابة الأسطر المتغيرة، والتغطية العامة عند `90%`، وجلسة Usability مع مدير غير تقني، ومصفوفة Viewports الكاملة، وتدقيق WCAG. كلها مذكورة بالتفصيل في «ما تبقى مفتوحًا صراحةً» داخل ملف المراجعة.

</div>
