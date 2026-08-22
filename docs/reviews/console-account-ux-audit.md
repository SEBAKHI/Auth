<div dir="rtl" lang="ar">

<style>
table, thead, tbody, tr, th, td {
  direction: rtl;
  text-align: right;
}
</style>

# تدقيق Usability وUX لتطبيقي Console وAccount

## 1. الملخص التنفيذي

- **Coverage:** `100.0%` — فُحصت Static جميع Routes/Pages الفعلية المكتشفة: `60/60` (`42` في Console و`18` في Account). عُدّ كل Parent route مع Index page له Route/Page واحدة لأنه ينتج URL واحدًا.
- **Runtime Coverage:** `8.3%` — فُحصت Runtime خمس Routes عامة فقط: Console `/` و`/login`، وAccount `/` و`/login` و`/register`.
- **EvidenceCoverage:** `100.0%` — كل Finding من `9/9` مرتبط بموضع كودي مثبت.
- **Findings حسب Priority:** `P0: 0`، `P1: 8`، `P2: 1`، `P3: 0`.
- **حساب المخاطر:** `RiskScore = round(20 × C × (0.50S + 0.30F + 0.20R), 1)`.
- **أخطر خمس مشكلات:** `UX-005` فقدان Search/Filter/Sort/Pagination عند العودة أو Reload (`80.0`)؛ `UX-007` فقدان تعديلات Template/Layout دون تحذير (`76.0`)؛ `UX-010` تنفيذ Publish/Unpublish دون Confirm (`74.0`)؛ `UX-011` رسائل الأخطاء الخام وغير القابلة للاسترداد (`74.0`)؛ `UX-012` تعارض Permission الخاصة بـPolicy بين Route وNavigation (`70.0`).
- **نتائج التحقق الآلي:** نجح `pnpm typecheck`، ونجحت `575/575` Unit Tests، ونجحت `5/5` اختبارات E2E العامة المحددة. فشل `pnpm lint` بـ`23 errors` و`1 warning`؛ لم تُحوّل مخالفات Lint إلى Findings إلا عندما كان لها أثر UX مباشر.
- **قيود التدقيق:** تعذر Browser plugin قبل فتح الصفحة بسبب قيد Trusted RPC داخلي؛ لم تتوفر بيانات Admin seeded أو Fixtures آمنة لفحص الشاشات المحمية؛ ولم تُنفذ mutations فعلية. لقطات المستخدم دعمت Findings الجديدة، لكنها لا تُحسب كجلسة Runtime تفاعلية كاملة.

## 2. Coverage Matrix

<div dir="rtl" align="right">

| التطبيق | Route/Page | المهمة الأساسية | Code Inspection | Runtime Inspection | الحالات المختبرة | الحالة | الملاحظات |
|---|---|---|---|---|---|---|---|
| Console | `/login` | تسجيل دخول مدير النظام | نعم | نعم | Anonymous، RTL switch، Desktop Chrome | مغطى Static + Runtime | `LoginPage`; نجح E2E للظهور وتبديل العربية إلى RTL |
| Console | `/forgot-password` | طلب إعادة ضبط كلمة المرور | نعم | لا — غير متحقق | Static: Form، Validation، Success/Error | مغطى Static | يتطلب API/Email fixture آمن |
| Console | `/force-password-change` | فرض تغيير كلمة المرور | نعم | لا — غير متحقق | Static: Form، Disabled، Error | مغطى Static | Route محمي؛ لا Session fixture |
| Console | `/` | Dashboard | نعم | نعم جزئيًا | Anonymous redirect إلى `/login` فقط | مغطى Static + Runtime جزئي | محتوى Dashboard المحمي غير متحقق Runtime |
| Console | `/users` | البحث وإدارة المستخدمين | نعم | لا — غير متحقق | Static: Loading/Empty/Error، Search، Sort، Pagination، Actions | مغطى Static | يحتاج Admin بصلاحيات وبيانات غير Production |
| Console | `/users/:id` | تفاصيل المستخدم وتعييناته | نعم | لا — غير متحقق | Static: Loading/Error/Not found، Tabs، Tables، Actions | مغطى Static | لا User fixture صالح |
| Console | `/roles` | إدارة Roles | نعم | لا — غير متحقق | Static: Search، Table، Create/Edit/Delete | مغطى Static | Route محمي |
| Console | `/roles/:id` | تفاصيل Role وتعييناته | نعم | لا — غير متحقق | Static: Loading/Error/Not found، Tables | مغطى Static | لا Role fixture صالح |
| Console | `/permissions` | إدارة Permissions | نعم | لا — غير متحقق | Static: Search، Table، CRUD | مغطى Static | Route محمي |
| Console | `/permissions/:id` | تفاصيل Permission وImplications | نعم | لا — غير متحقق | Static: Loading/Error/Not found، Dialogs، Tables | مغطى Static | لا Permission fixture صالح |
| Console | `/applications` | إدارة Applications | نعم | لا — غير متحقق | Static: Search، Sort، Pagination، CRUD | مغطى Static | Route محمي |
| Console | `/applications/:id` | إعداد Application والوصول | نعم | لا — غير متحقق | Static: Loading/Error/Not found، Tabs/Tables، Disable confirmation | مغطى Static | لا Application fixture صالح |
| Console | `/organizations` | إدارة كل Organizations أو Self-service | نعم | لا — غير متحقق | Static: Permission branch، Search، Pagination، CRUD | مغطى Static | السلوك يتبدل حسب `organizations:read` |
| Console | `/organizations/:id` | تفاصيل Organization وأعضائها | نعم | لا — غير متحقق | Static: Members، Invites، Apps، Transfer، Error | مغطى Static | لا Organization fixture صالح |
| Console | `/profile` | إدارة Profile/Security/Sessions | نعم | لا — غير متحقق | Static: Tabs، Forms، Loading/Error، Destructive flows | مغطى Static | Route محمي |
| Console | `/api-keys` | إنشاء/فحص/تدوير/Revoke API Keys | نعم | لا — غير متحقق | Static: Table، Dialogs، Secret reveal، Confirmations | مغطى Static | يمنع الاختبار الآمن إنشاء Secret حقيقي |
| Console | `/webhook-keys` | إدارة Webhook Keys | نعم | لا — غير متحقق | Static: Table، Dialogs، Rotate/Revoke | مغطى Static | يمنع الاختبار الآمن إنشاء Secret حقيقي |
| Console | `/audit-logs` | البحث والتصفية والتصدير | نعم | لا — غير متحقق | Static: Filters، Sort، Pagination، Export، Detail | مغطى Static | يحتاج Audit data وصلاحية Export |
| Console | `/notifications` | نظرة عامة على Notifications | نعم | لا — غير متحقق | Static: Loading/Error/Empty، Navigation cards | مغطى Static | Route محمي |
| Console | `/notifications/templates` | إدارة Notification Templates | نعم | لا — غير متحقق | Static: Search، Sort، Pagination، Create | مغطى Static | Route محمي |
| Console | `/notifications/templates/:id` | تحرير/نشر Template | نعم | لا — غير متحقق | Static: Loading/Error/Not found، Languages، Preview، Publish/Discard/Delete | مغطى Static | لا Template fixture؛ لم يُنفذ Publish |
| Console | `/notifications/layouts` | إدارة Notification Layouts | نعم | لا — غير متحقق | Static: Table، Create، Empty/Error | مغطى Static | Route محمي |
| Console | `/notifications/layouts/:id` | تحرير/نشر Layout | نعم | لا — غير متحقق | Static: Editor، Preview، Dirty state، Publish | مغطى Static | لا Layout fixture؛ لم يُنفذ Publish |
| Console | `/notifications/outbox` | مراقبة Delivery log وإعادة المحاولة | نعم | لا — غير متحقق | Static: Search، Status، Sort، Pagination، Retry | مغطى Static | لا Outbox fixture؛ لم يُنفذ Retry |
| Console | `/notifications/policy` | إدارة Privacy Policy revisions | نعم | لا — غير متحقق | Static: Search، Create/Clone/Publish/Notify | مغطى Static | Permission composition بها Finding `UX-012` |
| Console | `/notifications/policy/:id` | تحرير/معاينة/نشر Policy | نعم | لا — غير متحقق | Static: Loading/Error/Not found، Unsaved guard، RTL content، Publish confirm | مغطى Static | لا Revision fixture؛ لم يُنفذ Publish |
| Console | `/notification-templates` | Legacy redirect | نعم | لا — غير متحقق | Static: Redirect | مغطى Static | إلى `/notifications/templates` |
| Console | `/notification-templates/:id` | Legacy detail redirect | نعم | لا — غير متحقق | Static: ID-preserving redirect | مغطى Static | إلى `/notifications/templates/:id` |
| Console | `/notification-layouts` | Legacy redirect | نعم | لا — غير متحقق | Static: Redirect | مغطى Static | إلى `/notifications/layouts` |
| Console | `/notification-layouts/:id` | Legacy detail redirect | نعم | لا — غير متحقق | Static: ID-preserving redirect | مغطى Static | إلى `/notifications/layouts/:id` |
| Console | `/notification-outbox` | Legacy redirect | نعم | لا — غير متحقق | Static: Redirect | مغطى Static | إلى `/notifications/outbox` |
| Console | `/admin/secrets` | Legacy redirect | نعم | لا — غير متحقق | Static: Redirect | مغطى Static | إلى Secret Management داخل System Settings |
| Console | `/admin/platform-settings` | إعداد Branding/Platform | نعم | لا — غير متحقق | Static: Loading/Error، Forms، Upload/Save | مغطى Static | Route محمي |
| Console | `/admin/system-settings` | فهرس System Settings | نعم | لا — غير متحقق | Static: Loading/Error، Section cards | مغطى Static | Route محمي |
| Console | `/admin/system-settings/:sectionKey` | تحرير Settings section | نعم | لا — غير متحقق | Static: Validation، Conflict، Reset، Test، Unsaved guard | مغطى Static | لا Settings fixture آمن |
| Console | `/admin/system-settings/SecretManagement/keys` | إدارة Secrets الحساسة | نعم | لا — غير متحقق | Static: Challenge، OTP، Impact، Typed confirmation، Error | مغطى Static | لم تُنفذ عملية Secret |
| Console | `/two-factor` | إكمال Two-factor challenge | نعم | لا — غير متحقق | Static: OTP/Recovery code، Expiry، Retry | مغطى Static | لا Pending challenge fixture |
| Console | `/verify-email` | تأكيد البريد | نعم | لا — غير متحقق | Static: OTP، Resend، Success/Error | مغطى Static | لا Verification fixture |
| Console | `/accept-invitation` | تحويل Invitation إلى Account app | نعم | لا — غير متحقق | Static: Query-preserving external redirect | مغطى Static | لا Token صالح |
| Console | `/reset-password` | تحويل Reset إلى Account app | نعم | لا — غير متحقق | Static: Query-preserving external redirect | مغطى Static | لا Token صالح |
| Console | `/403` | Access denied | نعم | لا — غير متحقق | Static: Status screen، Home recovery | مغطى Static | لم يُفحص Runtime |
| Console | `*` | 404 recovery | نعم | لا — غير متحقق | Static: Status screen، Home recovery | مغطى Static | Catch-all |
| Account | `/login` | تسجيل الدخول إلى الحساب | نعم | نعم | Anonymous، Link إلى Register، Desktop Chrome | مغطى Static + Runtime | نجح E2E |
| Account | `/register` | إنشاء حساب | نعم | نعم | Empty submit، Client validation، Navigation | مغطى Static + Runtime | نجح E2E دون API mutation |
| Account | `/forgot-password` | طلب Reset | نعم | لا — غير متحقق | Static: Form، Validation، Success/Error | مغطى Static | يتطلب Email fixture آمن |
| Account | `/delete-account` | طلب حذف عام دون Login | نعم | لا — غير متحقق | Static: Email، OTP، Done، Error | مغطى Static | لم تُنفذ عملية حذف |
| Account | `/force-password-change` | تغيير كلمة مرور إلزامي | نعم | لا — غير متحقق | Static: Form، Validation، Error | مغطى Static | لا Session fixture |
| Account | `/` | تحويل إلى Profile بعد Auth | نعم | نعم جزئيًا | Anonymous redirect إلى `/login` فقط | مغطى Static + Runtime جزئي | Authenticated redirect إلى `/profile` غير متحقق |
| Account | `/profile` | Profile/Security/Sessions/Delete | نعم | لا — غير متحقق | Static: Tabs، Forms، Empty/Error، Confirmations | مغطى Static | Route محمي |
| Account | `/organizations` | Organizations الخاصة بالمستخدم | نعم | لا — غير متحقق | Static: Search، Empty/Error، Create/Delete | مغطى Static | Route محمي |
| Account | `/organizations/:id` | Members/Invites/Apps/Ownership | نعم | لا — غير متحقق | Static: Tables، Dialogs، Transfer، Error | مغطى Static | لا Organization fixture |
| Account | `/two-factor` | Two-factor أثناء Login | نعم | لا — غير متحقق | Static: OTP/Recovery، Expiry، Error | مغطى Static | لا Pending challenge fixture |
| Account | `/verify-email` | تأكيد البريد | نعم | لا — غير متحقق | Static: OTP، Resend، Success/Error | مغطى Static | لا Verification fixture |
| Account | `/accept-invitation` | قبول Invitation | نعم | لا — غير متحقق | Static: Anonymous/Auth branches، Register، Error | مغطى Static | لا Token/Org fixture |
| Account | `/reset-password` | تعيين كلمة مرور جديدة | نعم | لا — غير متحقق | Static: Invalid token، Form، Success/Error | مغطى Static | لا Reset token صالح |
| Account | `/account-recovery` | استعادة حساب Pending deletion | نعم | لا — غير متحقق | Static: Password/External/2FA branches، Error | مغطى Static | لا Pending-deletion fixture |
| Account | `/deletion-scheduled` | تأكيد جدولة الحذف | نعم | لا — غير متحقق | Static: Date/Days، Recovery guidance | مغطى Static | يحتاج نتيجة حذف حقيقية |
| Account | `/logout` | تأكيد Sign out | نعم | لا — غير متحقق | Static: Confirmation، Pending/Error | مغطى Static | Route يحتاج Session |
| Account | `/signed-out` | تأكيد انتهاء Session | نعم | لا — غير متحقق | Static: Status، Sign-in recovery | مغطى Static | لم يُفحص Runtime |
| Account | `*` | 404 recovery | نعم | لا — غير متحقق | Static: Status screen، Home recovery | مغطى Static | Catch-all |

</div>

## 3. Findings

<div dir="rtl" align="right">

| ID | التطبيق | Route/Page | العنصر أو الأمر | الوضع الحالي | الوضع المطلوب | الأثر على مدير النظام | الدليل أو القاعدة | موضع المشكلة في الكود | الملفات ذات العلاقة | S | F | R | C | RiskScore | Priority | Effort | معيار القبول |
|---|---|---|---|---|---|---|---|---|---|---:|---:|---:|---:|---:|---|---|---|
| UX-005 | Console | جميع List routes | حفظ Search/Filter/Sort/Pagination | **Fact:** الحالة في `useState` وتعود للقيم الابتدائية عند unmount/reload؛ `useSearchHandoff` يحذف `q` من URL ولا يزامن تغييرات المستخدم. | مزامنة query state في URL مع schema موحد، واستعادتها عند Back/Reload/Deep link. | بعد فتح record ثم العودة يفقد المدير موضعه ومرشحاته، ويكرر البحث في مهام عالية التكرار. | [NN/G: User Control and Freedom وFlexibility and Efficiency](https://www.nngroup.com/articles/ten-usability-heuristics/) | `Auth_UI/packages/ui/src/hooks/use-search-query.ts:23-40`; `Auth_UI/apps/console/src/pages/users/users-page.tsx:46-54` | `applications-page.tsx`، `organizations-admin-page.tsx`، `audit-logs-page.tsx`، `notification-templates-page.tsx`، `notification-outbox-page.tsx`، كل list state | 3 | 5 | 5 | 1.00 | 80.0 | P1 | L | اضبط Search+Filter+Sort+Page، افتح Detail ثم Back وأعد Reload؛ تعود الحالة والنتائج نفسها، مع URL قابل للمشاركة ولا يضيف History entry لكل keystroke. |
| UX-007 | Console | Notification Template/Layout detail | Unsaved changes | **Fact:** الصفحتان تحسبان `isDirty` لكن لا تستخدمان `useUnsavedChangesPrompt` الموجود والمستخدم في Policy/System Settings. | تفعيل guard للتنقل الداخلي و`beforeunload` مع Discard confirmation، ومسح dirty بعد Save ناجح. | قد يفقد المدير محتوى Template/Layout طويلًا عند ضغط Sidebar أو Reload دون تحذير. | [NN/G: Error Prevention وUser Control](https://www.nngroup.com/articles/ten-usability-heuristics/) | `Auth_UI/apps/console/src/pages/notifications/notification-template-detail-page.tsx:113`; `Auth_UI/apps/console/src/pages/notifications/notification-layout-detail-page.tsx:125` | `Auth_UI/packages/ui/src/hooks/use-unsaved-changes.tsx:18`، `notification-policy-detail-page.tsx:219` | 4 | 4 | 3 | 1.00 | 76.0 | P1 | S | مع تعديل غير محفوظ: Navigation/Reload يعرضان تحذيرًا؛ Cancel يحتفظ بالمحتوى وFocus؛ Discard ينتقل؛ Save الناجح لا يحذر. |
| UX-010 | Console | Template/Layout detail | Publish/Unpublish | **Fact:** Publish للـTemplate/Layout وUnpublish للـTemplate mutations مباشرة من click؛ Policy Publish وحده يستخدم `ConfirmDialog`. | توحيد الخطوات الحساسة عبر AlertDialog يذكر version/languages/القنوات والأثر، مع Destructive styling لـUnpublish. | نقرة واحدة قد تغيّر رسائل مستقبلية أو توقف Template live دون فرصة مراجعة. | [NN/G: Error Prevention](https://www.nngroup.com/articles/ten-usability-heuristics/)، [shadcn Alert Dialog](https://ui.shadcn.com/docs/components/radix/alert-dialog) | `Auth_UI/apps/console/src/pages/notifications/notification-template-detail-page.tsx:258,289`; `Auth_UI/apps/console/src/pages/notifications/notification-layout-detail-page.tsx:241` | `notification-policy-detail-page.tsx:403-410` كنمط مرجعي، `confirm-dialog.tsx` | 4 | 3 | 4 | 1.00 | 74.0 | P1 | S | لا Publish/Unpublish Request قبل Confirm؛ Dialog يذكر العنصر/version/languages؛ Cancel آمن؛ Pending يمنع التكرار؛ Success/Error واضحان. |
| UX-011 | Console + Account | جميع API mutations | رسائل الأخطاء | **Fact:** `getErrorMessage` يعرض `Error.message` أو Backend descriptions/detail/title الخام؛ معظم mutations ترسلها إلى Toast دون mapping محلي أو recovery action. | طبقة mapping من Error code/status إلى رسالة محلية واضحة، Field errors inline، وإجراء Retry/تصحيح عند إمكانه؛ fallback لا يكشف jargon. | المدير غير التقني قد يرى رسالة إنجليزية/تقنية مؤقتة ولا يعرف ما الذي يصححه أو هل العملية آمنة لإعادة المحاولة. | [NN/G: Help Users Recognize, Diagnose, and Recover from Errors](https://www.nngroup.com/articles/ten-usability-heuristics/) | `Auth_UI/packages/api/src/errors.ts:46-55`; `Auth_UI/apps/console/src/pages/users/user-form-dialog.tsx:140` | جميع `toast.error(getErrorMessage(...))` في Console/Account/Auth | 3 | 4 | 5 | 1.00 | 74.0 | P1 | L | اختبارات contract لكل error code الشائع باللغات السبع؛ Validation يظهر قرب الحقل؛ network/403/conflict يقدّم next step؛ لا raw exception للمستخدم. |
| UX-012 | Console | `/notifications/policy*` | Permission gating وDiscoverability | **Fact:** Route المجموعة `/notifications` ملفوف أولًا بـ`RequirePermission(notificationTemplates.read)`، ثم Policy يضيف داخله `RequirePermission(privacyPolicy.read)`. لذلك امتلاك `privacyPolicy.read` وحدها لا يكفي: الطلب يتوقف عند الحارس الخارجي ويصل المستخدم إلى `/403`. المشكلة نفسها في الاكتشاف؛ Sidebar وGlobal Search لا يعرضان Policy إلا مع Templates permission. | إزالة permission الموحدة من Parent layout، ثم تعريف permission مستقلة لكل فرع: Templates وLayouts وOutbox وPolicy. تُستخدم metadata واحدة لبناء Route guard وSidebar وTabs وGlobal Search، بدل تكرار الشروط في أربعة مواضع. النتيجة الدقيقة: Policy-only يرى ويفتح Policy فقط؛ Templates-only يرى فروع Templates فقط؛ من يملك الاثنين يرى الاثنين؛ ومن لا يملكهما لا يرى أيًا منهما. | موظف الخصوصية المصرح له لا يستطيع أداء عمله حاليًا رغم امتلاكه الصلاحية الصحيحة، وقد يطلب صلاحية Templates أوسع من حاجته كحل التفافي. الحل يعيد Least Privilege ويجعل ما يظهر في Navigation مطابقًا لما يفتحه Direct URL. | Route truth + [NN/G: Consistency and Standards](https://www.nngroup.com/articles/ten-usability-heuristics/) | `Auth_UI/apps/console/src/routes.tsx:304,403`; `Auth_UI/apps/console/src/lib/constants.ts:161`; `Auth_UI/apps/console/src/components/global-search/static-surfaces.ts:195-201` | `Auth_UI/apps/console/src/pages/notifications/components/notifications-tabs.tsx`، `Auth_UI/packages/auth/src/require-permission.tsx`، Route metadata جديدة مشتركة | 4 | 3 | 3 | 1.00 | 70.0 | P1 | M | اختبارات Role matrix تثبت أربع حالات: `privacy-only` يفتح list/detail ويجد Policy في Sidebar/Tabs/Search؛ `templates-only` لا يرى Policy؛ `both` يرى الجميع؛ `neither` لا يرى الوجهات ويستقبل `/403` عند Direct URL. لا توجد شروط permission مستقلة ومتعارضة بين Route وNavigation. |
| UX-021 | Console | Global Search في جميع Routes | Recent history وQuick navigation | **Fact:** عند فتح Global Search بلا query، الشرط في الكود يعرض `Recent` إذا كان السجل غير فارغ، ويعرض `Jump to` فقط إذا كان `recentRows.length === 0`. لذلك أول عملية محفوظة تخفي قائمة الانتقال السريع بالكامل، كما يظهر في اللقطة المرفقة. | عرض المجموعتين معًا دائمًا عند الخمول: `Recent` أولًا عندما توجد عناصر، ثم `Separator`، ثم `Jump to`. يبقى Clear history داخل مجموعة Recent ولا يؤثر في Quick navigation. | المدير الذي استخدم البحث مرة يفقد الاختصارات الثابتة للمستخدمين والتطبيقات والسياسات وسجلات التدقيق، ويضطر إلى تذكر الاسم وكتابته بدل الاختيار المباشر. | [NN/G: Recognition Rather Than Recall](https://www.nngroup.com/articles/ten-usability-heuristics/)، [shadcn Command](https://ui.shadcn.com/docs/components/radix/command) | `Auth_UI/apps/console/src/components/global-search/global-search.tsx:552-574` | `use-recent-searches.ts`، `static-surfaces.ts`، `global-search.test.tsx`، locale files | 2 | 5 | 5 | 1.00 | 70.0 | P1 | XS | افتح Global Search بحساب لديه Recent entries: تظهر مجموعتا `Recent` و`Jump to` في الجلسة نفسها؛ Clear يحذف Recent فقط؛ Quick navigation يبقى ظاهرًا ويفتح الوجهة الصحيحة. |
| UX-022 | Console | `/users/:id` و`/notifications/templates/:id` | Discoverability للإجراءات المخفية | **Fact:** صفحة المستخدم تضع إدارة الأدوار والصلاحيات وإعادة كلمة المرور وحالة الحساب والحذف داخل `DropdownMenu`، وصفحة القالب تضع `Test send` وDiscard وUnpublish/Delete خلف زر ellipsis بلا Label مرئي. اللقطتان المرفقتان تؤكدان أن الخيارات المهمة لا تظهر قبل فتح القائمة. | إنشاء Action surface مرئي يعرض جميع الإجراءات المسموحة حسب Permission: الإجراءات الآمنة والمتكررة مثل `Manage roles` و`Manage permissions` و`Send password reset` و`Test send` تظهر كأزرار مسماة في Header أو صف إجراءات؛ إجراءات الحالة تُجمع في قسم مسمى؛ وDelete يبقى في Danger group واضح لا كـPrimary action. عند ضيق العرض فقط يجوز نقلها إلى زر `Actions` مسمى، مع بقاء نفس الخيارات وعدم استخدام ellipsis مجهول. | المدير غير التقني قد لا يكتشف وظائف أساسية، خصوصًا `Test send`، أو يظن أن الصفحة لا تدعمها. إظهارها يقلل الاستكشاف العشوائي ويجعل الوظائف المتاحة حسب صلاحياته قابلة للمسح البصري. | [NN/G: Recognition Rather Than Recall](https://www.nngroup.com/articles/ten-usability-heuristics/)، [shadcn Dropdown Menu](https://ui.shadcn.com/docs/components/radix/dropdown-menu) | `Auth_UI/apps/console/src/pages/users/user-detail-page.tsx:617-723`; `Auth_UI/apps/console/src/pages/notifications/notification-template-detail-page.tsx:244-309` | `PageHeader`، `use-user-actions.ts`، `TestSendDialog`، permission checks، responsive action layout | 3 | 4 | 4 | 1.00 | 70.0 | P1 | M | في desktop تظهر كل action المسموح بها دون فتح Menu، ويكون `Test send` زرًا مسمى؛ في narrow viewport تظهر داخل `Actions` مسمى لا ellipsis؛ لا يظهر Action بلا Permission؛ Delete منفصل بصريًا ولا يصبح Primary. |
| UX-023 | Console | Template/Layout detail | Preview light/dark default | **Fact:** `PreviewPane` يهيئ `scheme` دائمًا إلى `"light"` ولا يقرأ `resolvedTheme`، لذلك تُفتح المعاينة فاتحة حتى عندما تكون Console داكنة، كما يظهر في اللقطة المرفقة. | قراءة `resolvedTheme` من `ThemeProvider` واستخدامه كقيمة ابتدائية لـ`PreviewScheme` عند فتح الصفحة. يبقى Toggle مستقلًا بعد ذلك: يستطيع المدير تغيير معاينة البريد يدويًا دون تغيير Theme الموقع، ولا يُعاد ضبط اختياره أثناء التحرير. لأن `PreviewPane` مشترك، يطبق السلوك نفسه على Template وLayout. | يبدأ المدير من تمثيل يخالف السياق البصري الحالي وقد يراجع نسخة Light ظنًا أنها الحالة المقصودة، بينما اختبار Dark يحتاج نقرة إضافية في كل مرة. | [shadcn Toggle Group](https://ui.shadcn.com/docs/components/radix/toggle-group)، Project `ThemeProvider` contract | `Auth_UI/apps/console/src/pages/notifications/components/preview-pane.tsx:47-50`; `Auth_UI/packages/ui/src/theme-provider.tsx:3-20` | `email-preview-frame.tsx`، `template-preview.tsx`، `notification-layout-detail-page.tsx`، Preview tests | 2 | 5 | 4 | 1.00 | 66.0 | P1 | XS | فتح Template أو Layout مع Console داكنة يحدد Dark ويضبط iframe `colorScheme=dark`؛ مع Console فاتحة يحدد Light؛ التغيير اليدوي يعمل ولا يغير Theme الموقع ولا يُمسح بسبب re-render. |
| UX-013 | Console + Account | Lists وDetails | Navigation controls | **Fact:** أسماء records وروابط التفاصيل في عدة Lists منفذة كـraw `<button>` يستدعي `navigate()`، مع أن النتيجة انتقال إلى URL جديد. المتصفح لذلك يتعامل معها كأوامر لا كروابط ولا يقدم خصائص Link المعتادة. | استبدال كل عنصر يفتح Route بـReact Router `Link` مع المحافظة على الشكل الحالي عبر `Button asChild` أو Link styling. إزالة `onClick+navigate` من هذه العناصر، وعدم تحويل أوامر Edit/Delete/فتح Dialog إلى Links. داخل الجداول يكون Link داخل خلية مخصصة، ولا تعتمد الصفحة على row click متداخل مع عنصر تفاعلي. | بعد الحل يستطيع المدير فتح عدة records في Tabs للمقارنة، استخدام Copy link وOpen in new tab وCtrl/⌘+click، ورؤية عنوان الوجهة في Status bar. Back/Forward وHistory يحتفظان بالسلوك المتوقع، بينما تبقى mutations أزرارًا واضحة. | [React Router Link](https://reactrouter.com/api/components/Link)، [NN/G: Consistency and Standards](https://www.nngroup.com/articles/ten-usability-heuristics/) | `Auth_UI/apps/console/src/pages/users/users-page.tsx:166-169`; `Auth_UI/apps/console/src/pages/applications/applications-page.tsx:134-137`; `Auth_UI/apps/console/src/pages/roles/roles-page.tsx:85-88` | `permissions-page.tsx`، `organizations*-page.tsx`، `user/role/application/permission-detail-page.tsx`، Notification list/overview pages، DataTable cells | 2 | 4 | 4 | 1.00 | 60.0 | P2 | M | كل record destination له `href` صحيح؛ click العادي ينتقل في Tab نفسه؛ Ctrl/⌘+click وMiddle click يفتحان Tab جديدًا؛ context menu يقدم Copy/Open؛ Buttons المتبقية تنفذ أوامر فقط؛ لا nested interactive controls في row. |

</div>

## 4. المشكلات المشتركة

<div dir="rtl" align="right">

| المجموعة | Findings | Routes/Components المتأثرة | الإصلاح المركزي المناسب |
|---|---|---|---|
| Query-state restoration | UX-005 | Users، Applications، Organizations، Audit Logs، Notification Templates/Outbox وبقية Lists | Hook/schema مركزي يقرأ ويكتب Search/Filters/Sort/Page/PageSize من URL مع defaults typed. |
| Publish-operation safety | UX-010 | Notification Template/Layout publish/unpublish | توحيد Confirm summary قبل تغيير الحالة المنشورة، مع عرض Version والنطاق ومنع Request المكرر أثناء Pending. |
| Error and recovery language | UX-007، UX-011 | Editors وكل API mutations | استخدام unsaved guard الموجود، وإنشاء error-code mapping مركزي محلي مع field-level recovery وretry guidance. |
| Permission-aware IA | UX-012 | Notifications Route tree، Sidebar، Tabs، Global Search | Parent الخاص بـNotifications يصبح Layout بلا permission عامة؛ كل Child يعلن permission الخاصة به في destination metadata واحدة تستخدمها Routes وSidebar وTabs وSearch. بهذا لا تعود `privacyPolicy.read` معتمدة ضمنيًا على `notificationTemplates.read`. |
| Search start state | UX-021 | `GlobalSearch`، Recent history، Quick navigation | عرض Recent وJump to كمجموعتين متجاورتين بدل شرط `either/or`؛ Clear history يؤثر في Recent فقط. |
| Action discoverability | UX-022 | User detail، Notification Template detail، `PageHeader` | Action surface مرئي حسب Permission: الأوامر المتكررة أزرار مسماة، إجراءات الحالة مجموعة ظاهرة، وDanger منفصل؛ `DropdownMenu` يصبح fallback للشاشات الضيقة فقط. |
| Route navigation semantics | UX-013 | Record names في Lists وDetails | كل وجهة Route تتحول إلى `Link` ذي `href` فعلي، بينما تبقى mutations وDialogs أزرارًا. النتيجة هي دعم Tabs وCopy link وbrowser context menu دون تغيير الشكل البصري. |
| Preview state | UX-023 | Template/Layout preview | تهيئة Preview scheme من `resolvedTheme` مرة عند فتح الصفحة مع السماح بتغيير المعاينة مستقلًا. |

</div>

## 5. خطة التنفيذ الحتمية

<div dir="rtl" align="right">

| الترتيب | Task ID | Findings المعالجة | Dependencies | الملفات المتوقعة | الإجراء | معيار القبول | Validation | Exit Criteria | المخاطر |
|---:|---|---|---|---|---|---|---|---|---|
| 1 | IMP-01 | UX-010 | لا شيء | Notification Template/Layout detail، `confirm-dialog.tsx`، locales، tests | إضافة Confirm summary موحد قبل Publish/Unpublish يوضح العنصر وVersion والنطاق. | لا Publish/Unpublish Request قبل Confirm، وPending يمنع الإرسال المكرر. | Unit mutation spies + E2E Cancel/Confirm + success/error assertions. | جميع سيناريوهات Publish وUnpublish تمر قبل الانتقال إلى Information Architecture. | قد تتغير صياغة أثر النشر؛ تثبت Product copy قبل التنفيذ. |
| 2 | IMP-02 | UX-012 | IMP-01 | `routes.tsx`، `constants.ts`، `static-surfaces.ts`، `notifications-tabs.tsx`، Route metadata، tests | إزالة Parent permission العامة وتعريف Permission واحدة لكل destination تُستخدم في Route/Nav/Tabs/Search. | Role matrix الأربع في UX-012 تعمل دون طلب Permission إضافية غير لازمة. | Router tests + Sidebar/Tabs/Search assertions + Direct URL 403 لكل Role. | لا اختلاف بين ما يظهر وما يفتح؛ عندها فقط يبدأ تعديل Search navigation. | تغيير Route nesting قد يؤثر Breadcrumbs والLegacy redirects. |
| 3 | IMP-03 | UX-021 | IMP-02 | `global-search.tsx`، `use-recent-searches.ts`، locales، tests | عرض Recent وJump to معًا، وفصل Clear history عن Quick navigation. | وجود سجل لا يخفي قائمة الانتقال السريع. | Component test بحالة recent فارغة/ممتلئة + visual snapshot EN/AR + destination assertions. | الحالات الثلاث: no history، history، cleared history تمر دون اختفاء Jump to. | زيادة طول القائمة؛ ضع cap واضحًا لـRecent واترك Quick navigation ثابتة. |
| 4 | IMP-04 | UX-005 | IMP-03 | `use-search-query.ts`، جميع List pages، query tests | URL query schema موحد لـSearch/Filters/Sort/Page/PageSize ومهاجرة Lists بالتتابع. | Back/Reload/Deep link يعيد الحالة والنتائج نفسها. | Unit encode/decode + E2E list→detail→Back + invalid params. | كل List paginated في Coverage Matrix مهاجر قبل تعديل Editors. | URLs قد تكبر؛ لا تخزن أسرارًا أو قيمًا غير قابلة للمشاركة. |
| 5 | IMP-05 | UX-007 | IMP-04 | Template/Layout detail، `use-unsaved-changes.tsx`، tests | توصيل guard الموجود وضبط dirty reset بعد Save. | لا فقد صامت للتعديلات عند Navigation أو Reload. | Navigation، Reload، language/tab changes، Save failure/success. | كل Editor ذو dirty state يملك guard أو autosave مثبت قبل إعادة ترتيب Actions. | Blocker قد يمنع Navigation البرمجي بعد Success إذا لم يُمسح dirty أولًا. |
| 6 | IMP-06 | UX-022 | IMP-05 | User detail، Template detail، `PageHeader`، action layout، locales، permission tests | نقل كل الإجراءات المتاحة من القوائم المخفية إلى Action surface مرئي، مع responsive fallback مسمى وفصل Danger. | Desktop يعرض جميع الإجراءات المسموحة؛ narrow viewport يستخدم `Actions` مسمى؛ `Test send` ظاهر مباشرة. | Permission matrix + desktop/mobile snapshots + click-path tests لكل Action + EN/AR. | لا Action متاح يوجد حصريًا داخل ellipsis؛ لا Action غير مصرح يظهر؛ عندها تبدأ معالجة Feedback. | ازدحام Header؛ استخدم grouping وwrap ولا ترفع Delete إلى Primary. |
| 7 | IMP-07 | UX-011 | IMP-06 | `errors.ts`، locales، forms/mutations، API error contracts، tests | mapping محلي حسب code/status، inline field errors، وRetry/correction guidance. | كل رسالة تصف المشكلة والخطوة التالية دون Backend jargon. | Contract tests لحالات 400/401/403/409/429/500/network وunknown fallback. | كل error class الشائع له Copy وRecovery مثبتان قبل تحويل Navigation controls. | Backend codes غير مستقرة؛ ثبّت contract واختبر unknown fallback. |
| 8 | IMP-08 | UX-013 | IMP-07 | List/detail pages، DataTable cells، React Router Links، tests | استبدال `button+navigate` بـ`Link` لكل Route destination، مع إبقاء mutations وDialogs كـButtons. | كل record destination يملك `href` فعليًا وسلوك المتصفح المعتاد. | Click، Ctrl/⌘+click، Middle click، context menu، Back/Forward، nested-control checks. | لا Button متبقٍ غرضه الوحيد فتح Route؛ عندها يبدأ ضبط Preview state. | Links داخل Table rows قد تتضارب مع row click؛ أزل الاعتماد على row click أو افصل الخلية التفاعلية. |
| 9 | IMP-09 | UX-023 | IMP-08 | `preview-pane.tsx`، `theme-provider.tsx`، Preview tests | تهيئة Preview scheme من `resolvedTheme` مرة عند فتح الصفحة، مع بقاء Toggle مستقلًا بعد الاختيار اليدوي. | Dark site يفتح Dark preview وLight site يفتح Light preview في Template وLayout. | Unit test لكل Theme + iframe `colorScheme` assertion + manual override + re-render test. | كل حالات Theme والاختيار اليدوي تمر قبل Regression النهائي. | مزامنة Theme باستمرار قد تمحو اختيار المستخدم؛ استخدم initial default لا forced binding. |
| 10 | IMP-10 | UX-005، UX-007، UX-010، UX-011، UX-012، UX-013، UX-021، UX-022، UX-023 | IMP-09 | Unit/E2E configs، visual snapshots، usability script، docs | Regression كاملة ثم جلسة Usability مع Admin غير تقني. | كل معيار قبول في نطاق الخطة مثبت ولا Regression في Permissions أو Navigation أو Actions أو Preview. | `pnpm lint` و`pnpm typecheck` و`pnpm test` وE2E، EN/AR، desktop/mobile، task completion metrics. | جميع checks ناجحة، Runtime Coverage للمهام المعدلة موثق، ولا blocker مفتوح قبل Release. | Runtime الآمن يحتاج seeded Roles وبيانات disposable؛ يمنع استخدام Production. |

</div>

## 6. Validation Checklist

- **المرحلة 1 — Publish safety:**
  - [x] Publish وUnpublish لا يرسلان Request قبل Confirm يعرض العنصر وVersion والنطاق.
  - [x] Cancel يغلق Dialog دون تغيير الحالة أو فقد Draft.
  - [x] Double click وNetwork retry لا ينتجان mutation مكررًا.
  - [x] تستخدم الاختبارات Fixtures معزولة فقط، ولا Production data.
- **المرحلة 2 — Permissions وInformation Architecture:**
  - [x] `privacy-only` يرى ويفتح Policy list/detail من Sidebar وTabs وSearch وDirect URL.
  - [x] `templates-only` لا يرى Policy، و`both` يرى الفرعين، و`neither` لا يرى أيًا منهما.
  - [x] Route وSidebar وTabs وGlobal Search تقرأ Permission من metadata واحدة.
  - [x] Legacy notification redirects وBreadcrumbs لم تتغير بعد فصل Parent guard.
- **المرحلة 3 — Search واستعادة سياق العمل:**
  - [x] Global Search يعرض Recent وJump to معًا عندما يوجد سجل.
  - [x] Clear history يحذف Recent فقط ولا يخفي Quick navigation.
  - [x] Search/Filters/Sort/Page تعود بعد Detail→Back وReload وDeep link.
  - [x] Invalid query params تعود إلى defaults دون crash أو redirect loop.
  - [x] Unsaved Template/Layout يحذر قبل Navigation أو Reload، وCancel يحتفظ بالقيم؛ Save failure يبقي المسودة وSave success يستأنف الانتقال المعلّق بأمان.
- **المرحلة 4 — Action discoverability وFeedback:**
  - [x] User detail يعرض جميع الإجراءات المسموحة في Action surface مرئي على desktop.
  - [x] `Test send` ظاهر كزر مسمى في Template detail، وليس حصريًا داخل ellipsis.
  - [x] Narrow viewport يستخدم زر `Actions` مسمى ويحتوي الخيارات نفسها؛ Delete يبقى في Danger group.
  - [x] Permission matrix تمنع ظهور Action غير مسموح ولا تخفي Action مسموحًا.
  - [x] Error messages محلية وتقدم correction أو Retry واضحًا لحالات 400/401/403/409/429/500/network.
- **المرحلة 5 — Links وPreview state:**
  - [x] كل record destination يملك `href` صحيحًا؛ Click وCtrl/⌘+click وMiddle click وCopy link تعمل.
  - [x] لا Link ينفذ mutation ولا Button متبقٍ غرضه الوحيد فتح Route.
  - [x] Dark Console يفتح Dark preview، وLight Console يفتح Light preview في Template وLayout — أول مرة فقط، ثم يحكم الاختيار المحفوظ.
  - [x] Preview Toggle اليدوي لا يغير Theme الموقع ولا يُمسح بعد re-render.
- **المرحلة 6 — Responsive وRTL والتحسينات البصرية:**
  - [x] Viewports: `320×568`، `375×667`، `768×1024`، `1280×720`، `1440×900` — وأُضيف `1279` لأن `1280` هو حدّ `xl` بالضبط لا داخله.
  - [x] Action surface يلتف دون overlap، وينتقل إلى responsive fallback في النقطة المحددة فقط — مثبت بزوج `1279/1280` الذي يؤكد طرفَي الحدّ.
  - [~] اتجاه الصفحة وهندسة الغلاف مثبتان في LTR وRTL (الشريط الجانبي على الجهة الصحيحة)؛ ترتيب كل زرّ وقائمة على حدة **لم يُختبر آليًا** ويبقى لجلسة المراجعة البصرية.
  - [x] النصوص الطويلة في EN/AR لا تخفي Action — اسم من ثمانين حرفًا بالعربية والإنجليزية عند `320` و`1440`، والإجراءات تبقى ظاهرة بلا تمدّد.
- **المرحلة 7 — Regression وUsability Validation:**
  - [x] `pnpm lint` و`pnpm typecheck` و`pnpm test` وE2E كلها تنجح لكامل النطاق: `887/887` وحدة، `21/21` Playwright، وبناء إنتاجي ناجح للتطبيقين.
  - [x] Visual snapshots لـUser actions وTemplate actions تمر في EN/AR؛ Global Search وPreview مغطاة باختبارات تفاعل لا بلقطات.
  - [ ] Admin غير تقني ينجز: فتح Quick navigation مع سجل، اكتشاف `Test send`، إدارة User action، مقارنة records في Tabs، وفتح Preview المطابق للموقع.
  - [ ] قياس task completion وerrors/backtracks/time، وإغلاق أي blocker قبل Release.
  - [ ] كل ادعاء Runtime مدعوم بـtest log أو جلسة موثقة؛ لقطات المستخدم وحدها لا ترفع Coverage.

## 7. الفجوات والقيود

<div dir="rtl" align="right">

| العنصر غير المختبر | السبب | أثره على الثقة | الخطوة المطلوبة لإغلاق الفجوة |
|---|---|---|---|
| Runtime متصل بBackend للشاشات المحمية | اختبارات Playwright المعزولة تثبت عقود العميل والواجهات دون credentials، لكن لا توجد seeded fixtures آمنة لرحلات Backend حقيقية | لا يمكن بعد إثبات التكامل الكامل مع قاعدة البيانات أو خدمات الإرسال | إنشاء isolated test tenant/DB وRoles تغطي permission matrix ثم تشغيل journeys دون Production data. |
| Browser inspection التفاعلي | Browser plugin فشل قبل Navigation برسالة Trusted RPC path؛ لم يُستخدم DevTools أو Browser بديل للادعاء بالمشاهدة | Responsive/RTL والتفاعل البصري غير متحققة خارج E2E المحدود | إصلاح Browser plugin ثم إعادة الفحص عبر viewport matrix، أو تشغيل جلسة Playwright مصدق عليها مع artifacts. |
| عمليات Publish/Rotate | رحلات Publish الحالية معزولة عبر network interception ولا يوجد disposable backend مثبت | حواف طلبات Cancel/Confirm/single-flight مثبتة في العميل، لكن persistence والـrollback الفعليان غير مثبتين | isolated disposable DB، mail sink، fake secrets، ثم اختبارات persistence/rollback دون Production data. |
| Dynamic record Routes | توجد fixtures معزولة لـTemplate/Layout، لكن لا IDs/fixtures لبقية السجلات الديناميكية | populated Template/Layout وعمليات الحفظ والنشر متحققة Runtime في العميل؛ بقية التدفقات المركبة غير متحققة | Seed minimal users/roles/apps/orgs/policies/outbox rows مع cleanup recoverable. |
| جميع اللغات السبع | E2E يغطي الإنجليزية وRTL العربية في Global Search، ولا يغطي المصفوفة الكاملة | الترجمة وbidi لبقية اللغات والشاشات الداخلية غير متحققين Runtime | locale matrix آلي + manual pass للـAR/UR/FA وlong-string pass للـFR/TR. |
| shadcn Project Context عبر CLI | `shadcn info/docs` داخل `Auth_UI` لم يحلل aliases workspace (`components`, `ui`, `lib`) | النسخة/الـpreset ثبتا من `components.json` و`pnpm-lock`; وثائق المكوّنات جُلبت من CLI خارج project والموقع الرسمي، لكن auto-detected context غير متحقق | إصلاح aliases التي يتوقعها CLI أو تشغيله بإعداد workspace مدعوم ثم مقارنة المكونات المثبتة مع registry. |

</div>

## 8. سجل التنفيذ الحالي — 2026-08-22

<div dir="rtl" align="right">

| Task | الحالة | الدليل الحتمي | البوابة التالية |
|---|---|---|---|
| IMP-01 — Publish-operation safety | مكتمل | Confirm/Cancel/single-flight/conflict مغطاة بوحدة وPlaywright معزول | اجتاز Gate 1 |
| IMP-02 — Permission-aware IA | مكتمل | Role matrix لأدوار `privacy-only` و`templates-only` و`both` و`neither` عبر Route/Sidebar/Tabs/Search | اجتاز Gate 2 |
| IMP-03 — Global Search start state | مكتمل | no-history/history/clear-history وEN/AR RTL مغطاة | اجتاز Gate 3 |
| IMP-04 — URL list state | مكتمل | schema typed موحد؛ هجرة 13 قائمة عليا و6 جداول مضمّنة و5 مجموعات Tabs؛ deep-link وBack/Forward والقيم غير الصالحة مغطاة | اجتاز Gate 4 |
| IMP-05 — Unsaved editor protection | مكتمل | Router blocker و`beforeunload` موحدان؛ Template/Layout يستخدمان snapshots للحفظ وإعادة تأسيس baseline دون سحق تعديل أحدث | اجتاز Gate 5 |
| IMP-06 — Permission-aware action surface | مكتمل | وصف Actions موحد لكل صفحة؛ Desktop يعرضها مباشرة وnarrow viewport يستخدم قائمة مسماة من المصدر نفسه؛ Permission matrix وDanger والفعل المباشر مغطاة | اجتاز Gate 6 |
| IMP-07 — Local error recovery | مكتمل | تصنيف code-first/status-fallback محلي؛ validation inline مع focus؛ Replay آمن لأخطاء network/server فقط؛ لا ProblemDetails خام | اجتاز Gate 7 |
| IMP-08 — Real record links | مكتمل | ‏23 وجهة سجل صارت روابط `<a href>` حقيقية، مثبتة بمصفوفة snapshot تغطي كل قائمة وكل جدول مضمّن؛ وPlaywright يثبت Ctrl-click وMiddle-click في متصفح فعلي | اجتاز Gate 8 |
| IMP-09 — Preview scheme | مكتمل | ‏`usePreviewScheme`: Console داكنة تفتح معاينة داكنة أول مرة، ثم الاختيار اليدوي يُحفظ ولا يتحرك بعدها؛ مثبت على `colorScheme` الخاص بالـiframe | اجتاز Gate 9 |
| IMP-10 — Regression وتحقق نهائي | مكتمل عدا جلسة Usability | `910/910` وحدة و`55/55` Playwright وlint/typecheck/build خضراء؛ بوابة الأسطر المتغيرة `90.35%` فوق `90%` | يبقى: جلسة مع شخص، ونطاق WCAG وكسل AppShell بانتظار قرار |

### تصحيح القياس — لماذا انخفض الرقم من `98.31%` إلى `84.99%`

الرقم القديم لم يكن خاطئًا في الحساب، بل في التعريف. كان `readIstanbul` يمنح **كل سطر تمتد عليه عبارة واحدة** رصيدَ تلك العبارة؛ فكتلة JSX من عشرين سطرًا تُحتسب عشرين سطرًا مغطى لأن سطرها الأول نُفِّذ مرة. بعد التصحيح يُحتسب سطر البداية وحده — وهو تعريف Istanbul نفسه لتغطية الأسطر، والمتسق مع عتبة `lines` في `vitest.config.ts`.

النتيجة: المقام هبط من `4016` إلى `806` سطرًا، والنسبة الصادقة `84.99%` (`685/806`). لم تنخفض التغطية الفعلية؛ انخفض التضخيم.

يبقى `121` سطرًا غير مغطى، أكبر تجمعاته: `application-detail-page` (`11`)، `notification-template-detail-page` (`9`)، ثم `user-detail` و`policy-detail` و`layout-detail` (`7` لكلٍّ). الوصول إلى `90%` يتطلب تغطية `41` سطرًا إضافيًا، وهي معالِجات إجراءات داخل صفحات التفاصيل. هذا دَين معلن، لا بوابة مُجتازة.

### عيوب أُغلقت أثناء المراجعة الخصومية

| العيب | الخطورة | ما كان يحدث |
|---|---|---|
| بوابة التغطية تمرّ فراغيًا خارج Windows | حرِج | `normalizeRepoPath` كانت تُصغّر الحروف على win32 فقط، بينما `verify()` تقارن بنطاقات مكتوبة صغيرة؛ فعلى macOS/Linux لا يطابق أي ملف، ويصبح `total === 0`، وتُرجع الدالة **نجاحًا** لأي تغيير |
| Validation السطرية لم تكن تعمل إطلاقًا | عالٍ | `getFieldErrors` كانت تقرأ قاموس ASP.NET فقط، وهو شكل لا يُصدره `ApiController.Problem` أبدًا: اسم الحقل يصل في `title` أو في `errors[].code`. كل رفض تحقق حقيقي كان يعرض تنبيهًا عامًا دون إبراز أي حقل |
| كتالوج الأخطاء بسبع لغات صار غير قابل للوصول | عالٍ | تجاهل `detail` أسقط الجُمل التي يكتبها الخادم لكل رمز — ومعها الحقائق التي لا يملكها العميل: موعد الحذف، وقت انتهاء القفل. مستخدم مقفول كان يُقال له «لا تملك صلاحية» |
| قوائم الفرز تخالف ما يقبله الخادم | عالٍ | `roles` و`status` و`owner` وغيرها ليست في `SortFields`؛ النقر على الترويسة يُرجع 400، وبعد IMP-04 صار الفرز الفاسد يعيش في الرابط ويُشارَك |
| اختبار اختصار «d» لا يمكن أن يفشل | عالٍ | حذف الحُرّاس الثلاثة جميعًا يُبقي الملف أخضر `5/5`؛ فحرف «d» داخل حقل بحث أو محرّر قالب كان قد يقلب سمة الموقع بلا اختبار يمنعه |
| قارئ محبوس في محرّر لا يستطيع الحفظ | متوسط | محرّر النص وأزرار المتغيّرات كانت بلا حارس صلاحية؛ فمن يملك `read` فقط يستطيع اتساخ المسودة، ثم يواجه حوار «تجاهل تعديلاتك؟» عن تعديل لم يكن مسموحًا أصلًا |
| `unwrap` تُعيد فشلًا على أنه نجاح | متوسط | استجابة فاشلة بجسم فارغ أو غير JSON تترك `error` غير معرَّف، فتُعاد `undefined` كبيانات وتعرض القائمة «لا نتائج» بدل خطأ |
| نص استثناء خام في المعاينة | متوسط | صفحتا المعاينة كانتا تعرضان `error.message`؛ انقطاع الشبكة يطبع «Failed to fetch» غير المترجمة في اللغات السبع |
| `Deactivate` ينفّذ من القائمة بلا سؤال | متوسط | إلغاء التفعيل يُخرج الحساب من كل الأجهزة، وكان يقع بنقرة واحدة في قائمة صفٍّ — مؤشر واحد بعيدًا عن الشخص الخطأ |
| حارس «عدم التمدّد الأفقي» لا يمكن أن يفشل | عالٍ | كان يقيس `document.documentElement`، والغلاف يقصّ الفائض عند مستويين (`SidebarProvider` و`SidebarInset` كلاهما `overflow-hidden`). حقنُ عنصر عرضه `3000px` أعطى `0` فمرّ التأكيد بينما التخطيط مكسور بمقدار `2625` بكسل؛ القياس انتقل إلى الحاوية نفسها |

### بصمة Gate 4

- `pnpm lint`: ناجح دون warnings.
- `pnpm typecheck`: ناجح لتطبيقي Console وAccounts.
- Unit/Integration: `71` ملفًا و`675/675` اختبارًا ناجحًا.
- Frontend changed-line coverage: `97.17%` (`2235/2300`) مقابل بوابة `90%`.
- Frontend global line coverage: `44.99%` (`3253/7230`)؛ ارتفعت من `36.81%` قبل إضافة اختبارات تكامل القوائم، لكنها تظل دينًا عامًا يُرفع تدريجيًا ولا تُعامل كاكتمال لتغطية المستودع.
- Playwright isolated: `12/12` ناجحة، وتشمل مراحل IMP-01 إلى IMP-04.
- Production build: ناجح؛ التحذير المتبقي تشغيلي غير حاجب عن chunks أكبر من `400 kB` ويُرحّل كدين أداء، لا كفشل وظيفي.

### عقد IMP-04 المنفذ

1. URL هو مصدر الحقيقة لـ`q/page/pageSize/sort/direction/filters`، مع namespaces للجداول المضمّنة.
2. قيم الصفحة وحجمها والفرز والفلاتر تمر عبر حدود وقوائم سماح قبل دخول query key أو API.
3. Search يستخدم `replace` ويعيد الصفحة إلى الأولى ذريًا؛ تغييرات page/pageSize/sort/filter تستخدم history entries قابلة لـBack/Forward.
4. القيم الافتراضية لا تُكتب؛ القيم الفاسدة تُصحح إلى URL canonical مع الحفاظ على parameters المملوكة لمكونات أخرى.
5. لا تُخزن secrets أو payloads حساسة في URL؛ الفلاتر النصية محدودة الطول والعدد.

### بصمة Gate 5

- `pnpm lint`: ناجح دون warnings.
- `pnpm typecheck`: ناجح لتطبيقي Console وAccounts.
- Unit/Integration: `72` ملفًا و`687/687` اختبارًا ناجحًا.
- Frontend changed-line coverage: `97.21%` (`2372/2440`) مقابل بوابة `90%`.
- Frontend global line coverage: `45.71%` (`3314/7250`)؛ ارتفعت من `44.99%` عند Gate 4، لكنها تظل دينًا عامًا معلنًا وليست ادعاء تغطية شاملة.
- Playwright isolated: `13/13` ناجحة، وتشمل Cancel/Discard وحالة navigation أثناء Save إلى جانب مراحل IMP-01 إلى IMP-04.
- Production build: ناجح؛ تحذير chunks الأكبر من `400 kB` ما زال دين أداء غير حاجب.

### عقد IMP-05 المنفذ

1. الانتقال إلى pathname مختلف يُحجب عند dirty أو save pending؛ تغييرات query/hash داخل الصفحة لا تُفقد المسودة ولا تُظهر تحذيرًا.
2. Reload وإغلاق التبويب والخروج الخارجي تستخدم `beforeunload` الأصلي للمتصفح، ويزال listener فور عودة الحالة إلى clean.
3. أثناء Save pending يبقى Cancel متاحًا ويُعطل Discard؛ نجاح الحفظ مع حالة clean يستأنف الانتقال المطلوب، وفشله يبقي المسودة والحوار لاتخاذ قرار صريح.
4. كل PUT يحمل snapshot ثابتًا من الحقول و`expectedModifiedAt`؛ لا يقرأ mutation قيمًا حية تغيرت بعد الضغط على Save.
5. استجابة PUT تصبح baseline وReact Query cache فورًا؛ إذا كتب المستخدم تعديلًا أحدث أثناء الطلب يُعاد تأسيسه فوق الاستجابة ويبقى dirty بدل أن يُستبدل.

### بصمة Gate 6

- `pnpm lint`: ناجح دون warnings.
- `pnpm typecheck`: ناجح لتطبيقي Console وAccounts.
- Unit/Integration: `74` ملفًا و`700/700` اختبارًا ناجحًا.
- Frontend changed-line coverage: `97.49%` (`2636/2704`) مقابل بوابة `90%`.
- Frontend global line coverage: `46.29%` (`3365/7268`)؛ ارتفعت من `45.71%` عند Gate 5، لكنها تظل دينًا عامًا معلنًا ولا تمثل تحقيق حد `90%` لكل المستودع.
- Playwright isolated: `15/15` ناجحة؛ تشمل permission-aware Actions على User desktop وTemplate mobile RTL مع baselines بصرية مستقرة، إضافة إلى Regression مراحل IMP-01 إلى IMP-05.
- Production build: ناجح؛ تحذير chunks الأكبر من `400 kB` ما زال دين أداء غير حاجب.
- ثُبّت اختبار Secrets البطيء على مهلة محلية `10s` بعد فشله مرتين تحت حمل coverage ونجاحه منفردًا؛ لم تتغير المهلة العامة ولم يُخفّض أي assertion.

### عقد IMP-06 المنفذ

1. كل صفحة تبني قائمة `PageAction[]` واحدة؛ السطح المرئي وقائمة الشاشات الضيقة يقرآن callbacks وحالات disabled/pending نفسها، فلا يوجد مساران قابلان للانحراف.
2. Permission filtering يحدث قبل العرض بحسب صلاحيات User وTemplate الحالية؛ هذا حاجز UX فقط وتبقى API هي سلطة التفويض النهائية.
3. عند `xl` تظهر الإجراءات كأزرار مسماة قابلة للالتفاف؛ دونه يظهر زر `Actions` مسمى يحمل المجموعة نفسها، ولا يوجد ellipsis غامض.
4. الإجراءات الخطرة منفصلة بصريًا وتستخدم destructive variant، مع الاحتفاظ بحوارات التأكيد ومسارات mutation القائمة.
5. حالات الانتظار تعطل النسختين وتعرض Spinner دون تلويث الاسم القابل للوصول؛ E2E يثبت عدم overflow ومسارات فتح Dialogs في EN desktop وAR mobile RTL.

### بصمة Gate 7

- `pnpm lint`: ناجح دون warnings.
- `pnpm typecheck`: ناجح لتطبيقي Console وAccounts.
- Unit/Integration: `75` ملفًا و`713/713` اختبارًا ناجحًا.
- Frontend changed-line coverage: `98.31%` (`3948/4016`) مقابل بوابة `90%`.
- Frontend global line coverage: `47.14%` (`3437/7290`)؛ ارتفعت من `46.29%` عند Gate 6، لكنها تظل دينًا عامًا معلنًا ولا تحقق شرط `90%` لكل المستودع.
- Playwright isolated: `17/17` ناجحة؛ أضيف مسار validation inline باللغة الإنجليزية ومسار transient Retry باللغة العربية على mobile، مع إعادة تشغيل Regression مراحل IMP-01 إلى IMP-06.
- Production build: ناجح؛ تحذير chunks الأكبر من `400 kB` ما زال دين أداء غير حاجب.
- اختبارات Form الجديدة تستخدم مهلة محلية `10s` تحت instrumentation فقط؛ لم تتغير المهلة العامة ولم يُخفّض أي assertion.

### عقد IMP-07 المنفذ

1. `ProblemDetails` يصنف أولًا من ErrorOr code المستقر ثم من HTTP status؛ unknown وcross-realm network failures يهبطان إلى fallback محلي آمن.
2. لا يُعرض نص استثناء ولا `Error.message` ولا مفتاح مورد غير مترجم. **صُحِّح بعد المراجعة:** جملة الخادم المترجمة تُفضَّل عندما يكون الرمز ضمن نطاقات `DomainErrors` المعروفة — لأنها مكتوبة لكل رمز بسبع لغات وتحمل حقائق لا يملكها العميل (موعد الحذف، وقت انتهاء القفل). تُستثنى الرموز التي تُدرج نصًا لم يكتبه الخادم: `Secret.ConnectionString*` و`Notification.RenderFailed`.
3. **صُحِّح بعد المراجعة:** اسم الحقل المرفوض يأتي من `title` (خطأ واحد) أو `errors[].code` (عدة أخطاء) — وهو ما يُصدره `ApiController.Problem` فعلًا؛ قاموس ASP.NET يبقى مدعومًا لنقاط النهاية التي تحمل DataAnnotations. تظهر الرسالة قرب الحقل، ويُضبط `aria-invalid` و`data-invalid` وينتقل focus إلى أول حقل مرفوض.
4. زر Retry يظهر داخل سطح الخطأ نفسه لأخطاء `network` و`server` فقط ويعيد snapshot الطلب ذاته؛ conflict وrate limit لا يقدمان replay مباشرًا قد يكرر أثرًا أو ضغطًا غير آمن.
5. الحالات ذات قرار تعافٍ خاص، ومنها duplicate email وstale data وinvalid challenge code وunreachable connection string، تملك mapping خاصًا بدل رسالة status عامة.
6. Copy المشكلة والخطوة التالية موجودة في اللغات السبع، واختبارات contract تثبت عدم تسرب Backend jargon عبر الحالات 400/401/403/409/429/500/network/unknown.

### ما تبقى مفتوحًا صراحةً

1. **بوابة الأسطر المتغيرة — مُغلقة.** `90.35%` (`740/819`) فوق بوابة `90%`، والأداة تخرج بـ`0`. أُضيف `23` اختبارًا عبر خمسة ملفات قائمة: معالِجات الفلاتر والتصدير في الجداول المضمّنة، وحقول تحرير القوالب والتخطيطات والسياسة، وإجراءات الوصول في صفحة المستخدم، ورابط الصعود الجديد. **الهامش رقيقان سطران فقط** فوق الحدّ الأدنى `738`.
2. **التغطية العامة `56.95%`** — ارتفعت من `47.14%` عند بداية هذه الجولة. عتبات `vitest.config.ts` رُفعت مرتين لتلاحقها، وهي الآن `56/45.1/48.9/56.9`. الوصول إلى `90%` يبقى دَين إصدار مستقلًا: الفجوة `2500` سطر تقريبًا، منها `1006` في `77` ملفًا تغطيتها صفر.
3. **جلسة Usability مع مدير غير تقني** — لم تُجرَ. تتطلب شخصًا، لا اختبارًا.
4. **مصفوفة Viewports والاتجاهين — مُغلق.** ستّ عروض (`320`، `375`، `768`، `1279`، `1280`، `1440`) × اتجاهين × أربعة مسارات، في `responsive-matrix.spec.ts`. الحزمة المعزولة صارت `42` اختبارًا بعد `21`. يبقى خارجه: ترتيب كل زرّ على حدة داخل RTL.
5. **تدقيق WCAG شامل وقياسات الأداء** — كما كان مؤجلًا، ولم يُنفَّذ.
6. **تحذير الحزمة — مُغلق.** الحزمة كانت `586 kB` من CodeMirror وحده. فصلُ `@codemirror/view` في `vendor-chunks.ts` يقسمها إلى `346` و`240` فيختفي التحذير. **كلفة شاشة الدخول: `44` بايت** (`926,093 → 926,137`)، وهي اسم حزمة إضافي في خريطة الاستيراد؛ الحزم الثماني الأخرى بصماتها لم تتغيّر. المحرّر لم يكن على مسار الدخول أصلًا ولا يزال. حارس `login-payload.spec.ts` يقيس ما يجلبه المتصفح فعلًا، وثبت أنه يفشل عند إعادة العطل التاريخي.

### ملاحظتان معماريتان لم تُعالَجا

- **شاشة الدخول تحمّل الواجهة المصادَقة كاملة.** `routes.tsx` يستورد `AppShell` استيرادًا ثابتًا لا كسولًا، فيدخل في الحزمة الأولى: البحث الشامل وcmdk وشريط التنقّل وقوائم Radix والتلميحات. الحصيلة `926 kB` من JavaScript زائد `162 kB` من CSS، يدفعها من لم يسجّل دخوله ولن يرى أيًّا منها. أكبر مكسب أداء متبقٍّ، وأكثرها مساسًا بالتوجيه — لم يُنفَّذ، وينتظر قرارًا.
- `notification-layout-detail-page` لم تنتقل إلى `PageActionSurface`؛ ما زالت تبني صف أزرار خاصًا بها في الترويسة، خلافًا لعقد IMP-06 «كل صفحة تبني قائمة `PageAction[]` واحدة».
- `Tabs` المرتبطة بـ`?tab=` (تفاصيل المستخدم والملف الشخصي) بقيت `Tabs` عمدًا: هي تبدّل لوحة داخل الوثيقة نفسها، لا تغيّر المسار، وعلاقة tab↔tabpanel هي ما يحتاجه قارئ الشاشة هناك. المحوَّل هو شريط أقسام الإشعارات وحده لأنه كان يعِد بلوحة لا وجود لها.

</div>

</div>
