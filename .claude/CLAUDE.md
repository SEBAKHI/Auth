# تعليمات هذا المستودع

> **نطاق هذا الملف:** ما يخصّ **هذا المستودع وحده** — مسارات، ارتباطات أدوات، حقائق مُتحقَّق منها هنا ولا تنطبق في غيره.
>
> أمّا ما هو صحيح في كل مشروع — بروتوكول الاستدلال والإخراج، والمبادئ المعمارية، والمهارات العامّة — فمكانه `~/.claude/CLAUDE.md` و`~/.claude/skills/`، ويُحمَّل تلقائيًّا مع هذا الملف. **لا تُعِد كتابته هنا:** نسختان من قاعدةٍ واحدة تنحرفان بأوّل تعديل يصيب إحداهما، بلا أن يفشل شيء.

## المهارات الإلزامية لهذا المستودع

| المهارة | متى |
|---------|-----|
| `dotnet-architecture` | قبل كتابة أو مراجعة أي كود C# — المبادئ العشرة شرطُ اكتمالٍ لا اقتراح |
| `frontend-playbook` | **قبل** أي كود تحت `Auth_UI/` — يقرّر الشكل والصحّة |
| `shadcn` | **بعده**، لأي عمل على المكوّنات — يورّد القطع. واقرأ **القسم التالي**: ارتباطاتُه في هذا المستودع تختلف عن الافتراضي |

**الترتيب مقصود:** `frontend-playbook` يقرّر الشكل، و`shadcn` يورّد القطع. مكوّنٌ اختيرَ بدقّة ثم وُضع في لوح تمرير يسحقه، أو رُبط بمفتاح ذاكرة غير موسوم، أو أُعيد ضبطه بـ`useEffect` — يبقى عيبًا، ولا يظهر أيٌّ من ذلك في فرقٍ على مستوى المكوّن.

المهارات الثلاث كلّها **على مستوى المستخدم** (`~/.claude/skills/`)، غير موردة في هذا المستودع. إن كانت مفقودة على جهازك فاستنسخها هناك — لا شيء في هذا المستودع سيجلبها لك.

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
